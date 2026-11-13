using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Civilization.Ecology
{
    internal struct PlantRenderPart : IComponentData
    {
        internal Entity Plant;
        internal float4x4 Model;
        internal float3 Position;
        internal int FruitIndex;
        internal bool IsLeafless;
    }

    [DisableAutoCreation]
    internal partial class VegetationRenderSystem : SystemBase
    {
        protected override void OnUpdate() { }

        internal void Synchronize(EntityQuery query)
        {
            EntityManager.CompleteDependencyBeforeRW<LocalToWorld>();
            EntityManager.CompleteDependencyBeforeRW<MaterialMeshInfo>();
            new UpdateVegetationRenderJob
            {
                Plants = GetComponentLookup<PlantState>(true),
                Parts = GetComponentTypeHandle<PlantRenderPart>(true),
                Transforms = GetComponentTypeHandle<LocalToWorld>(),
                MeshInfo = GetComponentTypeHandle<MaterialMeshInfo>()
            }.ScheduleParallel(query, default).Complete();
        }
    }

    [BurstCompile]
    internal struct UpdateVegetationRenderJob : IJobChunk
    {
        [ReadOnly] internal ComponentLookup<PlantState> Plants;
        [ReadOnly] internal ComponentTypeHandle<PlantRenderPart> Parts;
        internal ComponentTypeHandle<LocalToWorld> Transforms;
        internal ComponentTypeHandle<MaterialMeshInfo> MeshInfo;

        public void Execute(in ArchetypeChunk chunk, int chunkIndex, bool useEnabledMask, in v128 enabledMask)
        {
            var parts = chunk.GetNativeArray(ref Parts);
            var transforms = chunk.GetNativeArray(ref Transforms);
            for (int i = 0; i < chunk.Count; i++)
            {
                PlantRenderPart part = parts[i];
                PlantState plant = Plants[part.Plant];
                bool visible = part.FruitIndex >= 0 ? part.FruitIndex < plant.Servings : part.IsLeafless == plant.Leafless;
                if (chunk.IsComponentEnabled(ref MeshInfo, i) != visible)
                    chunk.SetComponentEnabled(ref MeshInfo, i, visible);
                if (!visible || plant.IsTree)
                    continue;

                transforms[i] = new LocalToWorld { Value = math.mul(float4x4.TRS(part.Position,
                    quaternion.identity, new float3(plant.Growth)), part.Model) };
            }
        }
    }

    internal sealed class VegetationRendering : IDisposable
    {
        private readonly World world;
        private readonly EntityManager manager;
        private readonly EcosystemOwner owner;
        private readonly EntityQuery renderQuery;
        private readonly VegetationRenderSystem renderSystem;
        private readonly List<Entity> standaloneEntities = new();
        private readonly Dictionary<GameObject, Model> grassModels = new();
        private readonly Species[] species;

        private readonly struct Part
        {
            internal readonly Entity Template;
            internal readonly float4x4 Matrix;
            internal Part(Entity template, float4x4 matrix) { Template = template; Matrix = matrix; }
        }

        private sealed class Model
        {
            internal readonly List<Part> Parts = new();
            internal Bounds Bounds;
        }

        private sealed class Species
        {
            internal Model Healthy, Bare, Fruit;
            internal float Scale;
            internal float FruitScale;
        }

        internal VegetationRendering(World world, EcosystemOwner owner, int profileCount)
        {
            this.world = world;
            this.owner = owner;
            manager = world.EntityManager;
            species = new Species[profileCount];
            renderSystem = world.GetOrCreateSystemManaged<VegetationRenderSystem>();
            renderQuery = manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<EcosystemOwner>(), ComponentType.ReadOnly<PlantRenderPart>(),
                    ComponentType.ReadWrite<LocalToWorld>(), ComponentType.ReadWrite<MaterialMeshInfo>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
            renderQuery.SetSharedComponentFilter(owner);
        }

        internal void Register(int index, PlantProfile profile)
        {
            Model healthy = ReadModel(profile.Prefab, false);
            Model fruit = ReadModel(profile.FruitPrefab, false);
            species[index] = new Species
            {
                Healthy = healthy, Bare = ReadModel(profile.LeaflessPrefab, false), Fruit = fruit,
                Scale = HeightScale(healthy, profile.MatureHeight), FruitScale = HeightScale(fruit, profile.FruitHeight)
            };
        }

        internal void AddPlant(Entity entity, in PlantState plant, Vector2 center, int fruitServings)
        {
            Species model = species[plant.Profile];
            var position = new float3(center.x, 0f, center.y);
            manager.GetBuffer<LinkedEntityGroup>(entity).Add(entity);
            AddParts(entity, model.Healthy, position, model.Scale, default, -1, false, plant);
            if (plant.IsTree)
                AddParts(entity, model.Bare, position, model.Scale, default, -1, true, plant);
            else if (model.Healthy != null && model.Fruit != null)
            {
                Vector3 extents = model.Healthy.Bounds.extents * model.Scale;
                for (int i = 0; i < fruitServings; i++)
                {
                    float angle = i * 2f * math.PI / fruitServings;
                    var offset = new float3(math.cos(angle) * extents.x * 0.55f,
                        extents.y * 2f - 0.08f, math.sin(angle) * extents.z * 0.55f);
                    AddParts(entity, model.Fruit, position, model.FruitScale, offset, i, false, plant);
                }
            }
        }

        internal void AddGrass(IReadOnlyList<GameObject> prefabs, int mapSize, int seed, int count)
        {
            if (count <= 0)
                return;

            var variants = new List<Model>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null)
                    continue;
                if (!grassModels.TryGetValue(prefab, out Model model))
                    grassModels.Add(prefab, model = ReadModel(prefab, true));
                if (model != null)
                    variants.Add(model);
            }
            if (variants.Count == 0)
                return;

            var random = new System.Random(seed ^ 0x251A643B);
            for (int i = 0; i < count; i++)
            {
                Model model = variants[random.Next(variants.Count)];
                var position = new float3((float)(random.NextDouble() - 0.5) * mapSize, 0f,
                    (float)(random.NextDouble() - 0.5) * mapSize);
                quaternion rotation = quaternion.RotateY((float)random.NextDouble() * 2f * math.PI);
                float4x4 placement = float4x4.TRS(position, rotation, new float3(1f));
                foreach (Part part in model.Parts)
                {
                    Entity render = manager.Instantiate(part.Template);
                    standaloneEntities.Add(render);
                    manager.SetComponentData(render, new LocalToWorld { Value = math.mul(placement, part.Matrix) });
                }
            }
        }

        internal void Synchronize() => renderSystem.Synchronize(renderQuery);

        internal void HideHarvestedFruit(Entity plant, int fruitIndex)
        {
            var group = manager.GetBuffer<LinkedEntityGroup>(plant, true);
            for (int i = 1; i < group.Length; i++)
            {
                Entity render = group[i].Value;
                if (manager.GetComponentData<PlantRenderPart>(render).FruitIndex == fruitIndex)
                    manager.SetComponentEnabled<MaterialMeshInfo>(render, false);
            }
        }

        private void AddParts(Entity parent, Model model, float3 position, float scale, float3 offset,
            int fruitIndex, bool isLeafless, in PlantState plant)
        {
            if (model == null)
                return;

            Vector3 center = model.Bounds.center;
            float y = fruitIndex >= 0 ? center.y : model.Bounds.min.y;
            float4x4 fit = float4x4.TRS(offset - new float3(center.x, y, center.z) * scale,
                quaternion.identity, new float3(scale));
            float growth = plant.IsTree ? 1f : plant.Growth;
            float4x4 placement = float4x4.TRS(position, quaternion.identity, new float3(growth));
            foreach (Part part in model.Parts)
            {
                Entity render = manager.Instantiate(part.Template);
                manager.GetBuffer<LinkedEntityGroup>(parent).Add(render);
                float4x4 matrix = math.mul(fit, part.Matrix);
                manager.AddComponentData(render, new PlantRenderPart { Plant = parent, Model = matrix,
                    Position = position, FruitIndex = fruitIndex, IsLeafless = isLeafless });
                manager.SetComponentData(render, new LocalToWorld { Value = math.mul(placement, matrix) });
                manager.SetComponentEnabled<MaterialMeshInfo>(render,
                    fruitIndex >= 0 ? fruitIndex < plant.Servings : isLeafless == plant.Leafless);
            }
        }

        private Model ReadModel(GameObject prefab, bool grass)
        {
            if (prefab == null)
                return null;

            var model = new Model();
            Transform root = prefab.transform;
            Matrix4x4 authored = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                Matrix4x4 matrix = authored * renderer.transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                int previousPartCount = model.Parts.Count;
                for (int i = 0; i < math.min(materials.Length, mesh.subMeshCount); i++)
                {
                    if (materials[i] == null)
                        continue;

                    Entity template = manager.CreateEntity(typeof(Prefab));
                    standaloneEntities.Add(template);
                    var description = grass
                        ? new RenderMeshDescription(ShadowCastingMode.Off, renderer.receiveShadows, layer: renderer.gameObject.layer,
                            renderingLayerMask: renderer.renderingLayerMask)
                        : new RenderMeshDescription(renderer);
                    RenderMeshUtility.AddComponents(template, manager, description,
                        new RenderMeshArray(new[] { materials[i] }, new[] { mesh }),
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, (ushort)i));
                    manager.AddSharedComponent(template, owner);
                    model.Parts.Add(new Part(template, ToFloatMatrix(matrix)));
                }

                if (model.Parts.Count == previousPartCount)
                    continue;
                Bounds bounds = TransformBounds(mesh.bounds, matrix);
                if (previousPartCount == 0) model.Bounds = bounds;
                else model.Bounds.Encapsulate(bounds);
            }
            return model.Parts.Count > 0 ? model : null;
        }

        private static float HeightScale(Model model, float height) =>
            model != null && model.Bounds.size.y > 0f ? height / model.Bounds.size.y : 1f;

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(Vector3.right * bounds.extents.x);
            Vector3 y = matrix.MultiplyVector(Vector3.up * bounds.extents.y);
            Vector3 z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
            var extent = new Vector3(math.abs(x.x) + math.abs(y.x) + math.abs(z.x),
                math.abs(x.y) + math.abs(y.y) + math.abs(z.y), math.abs(x.z) + math.abs(y.z) + math.abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), extent * 2f);
        }

        private static float4x4 ToFloatMatrix(Matrix4x4 m) => new(
            new float4(m.m00, m.m10, m.m20, m.m30), new float4(m.m01, m.m11, m.m21, m.m31),
            new float4(m.m02, m.m12, m.m22, m.m32), new float4(m.m03, m.m13, m.m23, m.m33));

        public void Dispose()
        {
            if (!world.IsCreated)
                return;
            renderQuery.Dispose();
            for (int i = standaloneEntities.Count - 1; i >= 0; i--)
                if (manager.Exists(standaloneEntities[i]))
                    manager.DestroyEntity(standaloneEntities[i]);
        }
    }
}
