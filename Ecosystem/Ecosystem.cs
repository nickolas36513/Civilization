using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Civilization.Ecology
{
    public sealed class Ecosystem : MonoBehaviour
    {
        private const int MinimumWorldSize = 40;
        private const int MaximumWorldSize = 256;
        private const int MaximumPlantLimit = 8192;
        private static readonly string[] Seasons = { "Spring", "Summer", "Autumn", "Winter" };
        private static readonly float[] GrowthRates = { 1.4f, 1f, 0.45f, 0f };
        private static readonly string[] WeatherNames = { "Wet", "Dry", "Mild", "Cold" };
        private static readonly Color[] GroundColors =
        {
            new Color(0.37f, 0.51f, 0.25f), new Color(0.45f, 0.51f, 0.24f),
            new Color(0.48f, 0.38f, 0.22f), new Color(0.63f, 0.69f, 0.68f)
        };

        [SerializeField] private NatureLibrary natureLibrary;

        [Header("World Generation")]
        [Tooltip("Length of each side in world units. Generation runs when Play starts.")]
        [Range(MinimumWorldSize, MaximumWorldSize)] public int worldSize = 120;
        [Tooltip("Use the same seed and settings to reproduce the starting world.")]
        public int randomSeed = 42;
        [Tooltip("Choose a new seed at startup. The active seed appears in Debug > Parameters.")]
        public bool randomizeSeedOnPlay;
        [Range(1, 12)] public int plantSpacing = 2;
        [Tooltip("Lower values give larger continuous habitat patches.")]
        [Range(0.01f, 0.25f)] public float habitatScale = 0.085f;
        [Range(0f, 1f)] public float plantDensity = 0.58f;
        [Tooltip("Chance that a plant in the mixed pass is a tree when both trees and bushes are available.")]
        [Range(0f, 1f)] public float treeChance = 0.28f;
        [FormerlySerializedAs("extraPines")]
        [Tooltip("Additional trees to place outside the center, subject to free space and the plant limit.")]
        [Range(0, MaximumPlantLimit)] public int extraTrees = 72;
        [Tooltip("Half-width of the central area excluded from the extra tree pass.")]
        [Min(0f)] public float outerTreeClearance = 11f;
        [Range(0, MaximumPlantLimit)] public int maxPlants = 300;
        [Range(0, MaximumPlantLimit)] public int maxBushes = 90;
        [Tooltip("Reserve space for this many bushes. Below this count, mature bushes spread by runners in spring.")]
        [Range(0, MaximumPlantLimit)] public int bushRecoveryTarget = 12;

        public int WorldSize => Mathf.Clamp(worldSize, MinimumWorldSize, MaximumWorldSize);
        public int ActiveSeed { get; private set; }

        internal readonly struct Bush
        {
            internal readonly int Id;
            internal readonly Vector2 Position;
            internal readonly int Servings;

            internal Bush(int id, Vector2 position, int servings)
            {
                Id = id;
                Position = position;
                Servings = servings;
            }
        }

        private readonly Dictionary<Vector2Int, Entity> plants = new();
        private readonly List<PlantProfile> profiles = new();
        private readonly Dictionary<PlantProfile, int> profileIndices = new();
        private readonly List<Bush> nearbyBushes = new();
        private readonly List<PlantProfile> treeProfiles = new();
        private readonly List<PlantProfile> bushProfiles = new();
        private readonly HashSet<int> bushSeedCells = new();
        private World entityWorld;
        private EntityManager entityManager;
        private EntityArchetype plantArchetype, seedArchetype;
        private EcosystemOwner owner;
        private EntityQuery plantQuery, seedQuery;
        private NativeArray<byte> habitat;
        private NativeArray<PlantRules> rules;
        private NativeList<PlantEvent> plantEvents;
        private NativeList<ReadySeed> readySeeds;
        private NativeArray<PlantTotals> chunkTotals;
        private PlantTotals totals;
        private VegetationRendering rendering;
        private bool visualsDirty;
        private System.Random random;
        private ProceduralWorldGenerator generator;
        private OccupancyGrid grid;
        private int day;
        private int nextId = 1;
        private int nextSeedOrder;
        private int Season => day % Seasons.Length;
        private float pineDays;
        internal float PineLifecycleSpeed { get; set; } = 1f;
        internal int PineSaplings => totals.Saplings;
        internal int PineYoung => totals.Young;
        internal int PineMature => totals.Mature;
        internal int PineOld => totals.Old;
        internal int PineSeeds { get; private set; }
        internal int LeaflessPines => totals.Leafless;
        internal int PinesLost { get; private set; }
        internal string SeasonName => Seasons[Season];
        internal string WeatherName => WeatherNames[Season];
        internal int BushCount { get; private set; }
        internal int AvailableFruit => totals.Fruit;
        internal int PlantLimit => Mathf.Clamp(maxPlants, 0, MaximumPlantLimit);
        internal int BushLimit => Mathf.Clamp(maxBushes, 0, PlantLimit);
        private int BushReserve => bushProfiles.Count == 0 ? 0 : Mathf.Clamp(bushRecoveryTarget, 0, BushLimit);
        private int TreeSlotsAvailable => Mathf.Min(PlantLimit - plants.Count, PlantLimit - BushReserve - (plants.Count - BushCount));
        internal int ConeBearingPines => totals.ConeBearing;
        internal bool IsFruitSeason => Season == 1 || Season == 2;
        internal Color GroundColor => GroundColors[Season];
        internal int PlantCount => plants.Count;
        private int fruitBushCount;
        private static readonly Vector2 WestStarterBush = new(-2.5f, -3.5f);
        private static readonly Vector2 EastStarterBush = new(6.5f, -2.5f);

        internal void Initialize(OccupancyGrid grid, int grassPatches)
        {
            this.grid = grid;
            if (natureLibrary != null)
                natureLibrary.CollectPlantProfiles(treeProfiles, bushProfiles);
            else
                Debug.LogWarning("Assign a Nature Library to the Ecosystem to populate trees and bushes.", this);

            ActiveSeed = randomizeSeedOnPlay ? System.Guid.NewGuid().GetHashCode() : randomSeed;
            generator = new ProceduralWorldGenerator(grid, ActiveSeed, habitatScale);
            random = new System.Random(ActiveSeed);
            InitializeEntities();
            day = 1; // Begin in summer with food available to the founders.
            PlantProfile starter = bushProfiles.Find(profile => profile.FruitServings > 0);
            if (starter == null && bushProfiles.Count > 0)
                starter = bushProfiles[0];
            if (starter != null)
            {
                AddPlant(grid.WorldToCell(WestStarterBush), starter, false, true, true);
                AddPlant(grid.WorldToCell(EastStarterBush), starter, false, true, true);
            }
            InitializePlants();
            PlantOuterTrees();
            if (natureLibrary != null)
                rendering.AddGrass(natureLibrary.Grass, grid.Size, ActiveSeed, grassPatches);
        }



        // Resident ticks consume this shared result before another resident scans.
        internal List<Bush> FindNearbyBushes(Vector2 position, float radius)
        {
            nearbyBushes.Clear();
            var indices = grid.FindNearbyCells(position, radius, CellOccupancy.Bush);
            for (int i = 0; i < indices.Length; i++)
            {
                Vector2Int cell = grid.CellFromIndex(indices[i]);
                PlantState plant = entityManager.GetComponentData<PlantState>(plants[cell]);
                nearbyBushes.Add(new Bush(plant.Id, grid.CellCenter(cell), plant.Servings));
            }
            return nearbyBushes;
        }

        internal bool TryReadBush(Vector2 position, int id, out Bush bush)
        {
            Vector2Int cell = grid.WorldToCell(position);
            if (plants.TryGetValue(cell, out Entity entity))
            {
                PlantState plant = entityManager.GetComponentData<PlantState>(entity);
                if (!plant.IsTree && plant.Id == id)
                {
                    bush = new Bush(id, position, plant.Servings);
                    return true;
                }
            }
            bush = default;
            return false;
        }

        internal bool TryHarvest(Bush bush)
        {
            Vector2Int cell = grid.WorldToCell(bush.Position);
            if (!plants.TryGetValue(cell, out Entity entity))
                return false;
            PlantState plant = entityManager.GetComponentData<PlantState>(entity);
            if (plant.IsTree || plant.Id != bush.Id || plant.Servings <= 0)
                return false;
            plant.Servings--;
            totals.Fruit--;
            entityManager.SetComponentData(entity, plant);
            if (!visualsDirty)
                rendering.HideHarvestedFruit(entity, plant.Servings);
            return true;
        }

        private bool CanPlant(Vector2Int cell) => grid.IsAvailable(OccupancyGrid.SingleCell(cell));

        private void InitializePlants()
        {
            if (treeProfiles.Count == 0 && bushProfiles.Count == 0)
                return;

            int outerBudget = treeProfiles.Count == 0 ? 0 : Mathf.Clamp(extraTrees, 0, Mathf.Max(0, TreeSlotsAvailable));
            int mixedLimit = PlantLimit - outerBudget;
            foreach (var (cell, isTree) in generator.MixedPlants(plantSpacing, plantDensity, treeChance))
            {
                if (plants.Count >= mixedLimit)
                    break;
                bool pine = treeProfiles.Count > 0 && (isTree || bushProfiles.Count == 0);
                AddPlant(cell, PickProfile(pine ? treeProfiles : bushProfiles), pine, true);
            }
        }

        private void PlantOuterTrees()
        {
            int remaining = Mathf.Clamp(extraTrees, 0, Mathf.Max(0, TreeSlotsAvailable));
            if (remaining == 0 || treeProfiles.Count == 0)
                return;

            foreach (Vector2Int cell in generator.OuterTrees(outerTreeClearance))
            {
                if (!CanPlant(cell) || HasPlantNearby(cell))
                    continue;

                AddPlant(cell, PickProfile(treeProfiles), true, true);
                if (--remaining == 0)
                    break;
            }
        }

        private bool HasPlantNearby(Vector2Int cell)
        {
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                if (plants.ContainsKey(cell + new Vector2Int(x, z)))
                    return true;
            return false;
        }

        private PlantProfile PickProfile(List<PlantProfile> profiles) => profiles.Count switch
        {
            0 => null,
            1 => profiles[0],
            _ => profiles[random.Next(profiles.Count)]
        };

        private void InitializeEntities()
        {
            entityWorld = World.DefaultGameObjectInjectionWorld;
            if (entityWorld == null || !entityWorld.IsCreated)
                throw new System.InvalidOperationException("The Ecosystem requires the default Unity Entities world.");

            entityManager = entityWorld.EntityManager;
            owner = new EcosystemOwner { Value = entityManager.CreateEntity() };
            plantArchetype = entityManager.CreateArchetype(typeof(PlantState), typeof(EcosystemOwner), typeof(LinkedEntityGroup));
            seedArchetype = entityManager.CreateArchetype(typeof(PlantSeed), typeof(EcosystemOwner));
            plantQuery = entityManager.CreateEntityQuery(ComponentType.ReadWrite<PlantState>(), ComponentType.ReadOnly<EcosystemOwner>());
            seedQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlantSeed>(), ComponentType.ReadOnly<EcosystemOwner>());
            plantQuery.SetSharedComponentFilter(owner);
            seedQuery.SetSharedComponentFilter(owner);

            foreach (PlantProfile profile in treeProfiles)
                RegisterProfile(profile);
            foreach (PlantProfile profile in bushProfiles)
                RegisterProfile(profile);

            rules = new NativeArray<PlantRules>(profiles.Count, Allocator.Persistent);
            plantEvents = new NativeList<PlantEvent>(Mathf.Max(1, PlantLimit), Allocator.Persistent);
            readySeeds = new NativeList<ReadySeed>(Mathf.Max(1, PlantLimit), Allocator.Persistent);
            rendering = new VegetationRendering(entityWorld, owner, profiles.Count);
            for (int i = 0; i < profiles.Count; i++)
            {
                rules[i] = new PlantRules(profiles[i]);
                rendering.Register(i, profiles[i]);
            }

            // Cache only the habitat decisions used by the jobs, one byte per map cell.
            habitat = new NativeArray<byte>(grid.CellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < habitat.Length; i++)
            {
                float value = generator.Habitat(grid.CellFromIndex(i));
                habitat[i] = (byte)((value >= 0.39f ? PlantLifecycle.SeedHabitat : 0)
                    | (value < 0.48f ? PlantLifecycle.DryTree : 0)
                    | (value < 0.57f ? PlantLifecycle.DryBush : 0));
            }
        }

        private void RegisterProfile(PlantProfile profile)
        {
            if (profileIndices.TryAdd(profile, profiles.Count))
                profiles.Add(profile);
        }

        private void AddPlant(Vector2Int cell, PlantProfile profile, bool tree, bool initial, bool mature = false, float seedlingGrowth = 0.12f)
        {
            if (profile == null || !CanEstablish(cell, tree))
                return;
            if (!grid.TryOccupy(OccupancyGrid.SingleCell(cell), tree ? CellOccupancy.Pine : CellOccupancy.Bush))
                return;

            int id = nextId++;
            int lifespan = random.Next(profile.MinimumLifespan, profile.MaximumLifespan + 1);
            uint randomState = math.hash(new uint2(unchecked((uint)ActiveSeed), (uint)id));
            var plant = new PlantState
            {
                Id = id,
                Cell = cell.y * grid.Size + cell.x,
                Profile = profileIndices[profile],
                IsTree = tree,
                Lifespan = lifespan,
                Growth = mature ? 1f : initial ? Mathf.Lerp(profile.MinimumStartingGrowth, 1f, (float)random.NextDouble()) : seedlingGrowth,
                Age = initial ? InitialAge(tree, profile, lifespan) : 0f,
                RandomState = randomState != 0 ? randomState : 1u
            };
            PlantRules profileRules = rules[plant.Profile];
            PlantLifecycle.RefreshSeason(ref plant, profileRules, Season, PlantLifecycle.IsDry(plant, habitat[plant.Cell], Season));

            Entity entity = entityManager.CreateEntity(plantArchetype);
            entityManager.SetSharedComponent(entity, owner);
            entityManager.SetComponentData(entity, plant);
            plants.Add(cell, entity);
            rendering.AddPlant(entity, plant, grid.CellCenter(cell), profileRules.FruitServings);
            totals.Count(plant, profileRules, Season);
            if (!tree)
            {
                BushCount++;
                if (profileRules.FruitServings > 0)
                    fruitBushCount++;
            }
        }

        private int InitialAge(bool tree, PlantProfile profile, int lifespan)
        {
            return tree
                ? random.Next(Mathf.Min(profile.MaturityDays, lifespan - 1), lifespan)
                : random.Next(0, Mathf.Min(65, lifespan));
        }

        internal void AdvanceDay()
        {
            day++;
            pineDays += PineLifecycleSpeed;
            int chunkCount = plantQuery.CalculateChunkCountWithoutFiltering();
            if (!chunkTotals.IsCreated || chunkTotals.Length < chunkCount)
            {
                if (chunkTotals.IsCreated) chunkTotals.Dispose();
                chunkTotals = new NativeArray<PlantTotals>(Mathf.Max(1, chunkCount), Allocator.Persistent);
            }
            for (int i = 0; i < chunkCount; i++)
                chunkTotals[i] = default;

            plantEvents.Clear();
            if (plantEvents.Capacity < plants.Count)
                plantEvents.Capacity = plants.Count;

            new AdvancePlantsJob
            {
                Plants = entityManager.GetComponentTypeHandle<PlantState>(false),
                Entities = entityManager.GetEntityTypeHandle(),
                Profiles = rules,
                Habitat = habitat,
                Occupancy = grid.Cells,
                ChunkTotals = chunkTotals,
                Events = plantEvents.AsParallelWriter(),
                Day = day,
                Season = Season,
                GridSize = grid.Size,
                TreeDays = pineDays,
                TreeSpeed = PineLifecycleSpeed,
                GrowthRate = GrowthRates[Season],
                GrowBushRunners = Season == 0 && BushCount < BushReserve
            }.ScheduleParallel(plantQuery, default).Complete();

            totals = default;
            for (int i = 0; i < chunkCount; i++)
                totals.Add(chunkTotals[i]);

            // Parallel worker completion order must not affect placement or the last bush's regrowth.
            var events = plantEvents.AsArray();
            events.Sort();
            foreach (PlantEvent plantEvent in events)
            {
                if (plantEvent.Kind == PlantEventKind.Death)
                    RemoveOrRegrow(plantEvent.Entity);
                else if (plantEvent.Kind == PlantEventKind.Seed)
                    AddSeed(plantEvent.Seed);
            }

            if (Season == 0)
            {
                Germinate();
                foreach (PlantEvent plantEvent in events)
                {
                    if (BushCount >= BushReserve)
                        break;
                    if (plantEvent.Kind == PlantEventKind.Runner)
                    {
                        PlantSeed runner = plantEvent.Seed;
                        AddPlant(grid.CellFromIndex(runner.Cell), profiles[runner.Profile], false, false, seedlingGrowth: 0.45f);
                    }
                }
            }
            visualsDirty = true;
        }

        private void RemoveOrRegrow(Entity entity)
        {
            PlantState plant = entityManager.GetComponentData<PlantState>(entity);
            PlantRules profileRules = rules[plant.Profile];
            bool bearsFruit = !plant.IsTree && profileRules.FruitServings > 0;
            if (!plant.IsTree && BushLimit > 0 && (BushCount == 1 || (bearsFruit && fruitBushCount == 1)))
            {
                plant.Age = 0f;
                plant.Growth = 1f;
                PlantLifecycle.RefreshSeason(ref plant, profileRules, Season, PlantLifecycle.IsDry(plant, habitat[plant.Cell], Season));
                entityManager.SetComponentData(entity, plant);
                totals.Count(plant, profileRules, Season);
                return;
            }

            if (plant.IsTree)
                PinesLost++;
            else
            {
                BushCount--;
                if (bearsFruit) fruitBushCount--;
            }

            Vector2Int cell = grid.CellFromIndex(plant.Cell);
            plants.Remove(cell);
            grid.Release(OccupancyGrid.SingleCell(cell), plant.IsTree ? CellOccupancy.Pine : CellOccupancy.Bush);
            entityManager.DestroyEntity(entity);
        }

        private void AddSeed(PlantSeed seed)
        {
            if (!seed.IsTree && (bushSeedCells.Count >= BushLimit * 3 || !bushSeedCells.Add(seed.Cell)))
                return;

            seed.Order = nextSeedOrder++;
            Entity entity = entityManager.CreateEntity(seedArchetype);
            entityManager.SetSharedComponent(entity, owner);
            entityManager.SetComponentData(entity, seed);
            if (seed.IsTree) PineSeeds++;
        }

        private void Germinate()
        {
            int count = seedQuery.CalculateEntityCount();
            if (count == 0)
                return;

            readySeeds.Clear();
            if (readySeeds.Capacity < count)
                readySeeds.Capacity = count;
            new FindReadySeedsJob
            {
                Seeds = entityManager.GetComponentTypeHandle<PlantSeed>(true),
                Entities = entityManager.GetEntityTypeHandle(),
                Ready = readySeeds.AsParallelWriter(),
                Day = day,
                TreeDays = pineDays,
                TreeGrowthEnabled = PineLifecycleSpeed > 0f
            }.ScheduleParallel(seedQuery, default).Complete();

            var ready = readySeeds.AsArray();
            ready.Sort();
            foreach (ReadySeed entry in ready)
            {
                PlantSeed seed = entry.Seed;
                Vector2Int cell = grid.CellFromIndex(seed.Cell);
                // Ready bush seeds wait in free cells if the population cap is temporarily full.
                if (!seed.IsTree && CanPlant(cell) && !HasPlantCapacity(false))
                    continue;

                entityManager.DestroyEntity(entry.Entity);
                if (seed.IsTree) PineSeeds--;
                else bushSeedCells.Remove(seed.Cell);
                AddPlant(cell, profiles[seed.Profile], seed.IsTree, false);
            }
        }

        private bool HasPlantCapacity(bool tree) => tree
            ? TreeSlotsAvailable > 0
            : plants.Count < PlantLimit && BushCount < BushLimit;

        private bool CanEstablish(Vector2Int cell, bool tree) => HasPlantCapacity(tree) && CanPlant(cell);

        internal void SynchronizeVisuals()
        {
            if (!visualsDirty)
                return;
            visualsDirty = false;
            rendering.Synchronize();
        }

        internal void DisposeRuntime()
        {
            if (entityWorld != null && entityWorld.IsCreated)
            {
                foreach (var entry in plants)
                {
                    if (entityManager.Exists(entry.Value))
                        entityManager.DestroyEntity(entry.Value);
                }
                if (!seedQuery.Equals(default(EntityQuery)))
                {
                    entityManager.DestroyEntity(seedQuery);
                    seedQuery.Dispose();
                }
                if (!plantQuery.Equals(default(EntityQuery)))
                    plantQuery.Dispose();
                rendering?.Dispose();
                if (entityManager.Exists(owner.Value))
                    entityManager.DestroyEntity(owner.Value);
            }
            foreach (Vector2Int cell in plants.Keys)
                if (grid.TryGetOccupancy(cell, out CellOccupancy occupancy))
                    grid.Release(OccupancyGrid.SingleCell(cell), occupancy);

            entityWorld = null;
            plantQuery = default;
            seedQuery = default;
            rendering = null;
            plants.Clear();
            profiles.Clear();
            profileIndices.Clear();
            treeProfiles.Clear();
            bushProfiles.Clear();
            bushSeedCells.Clear();
            nearbyBushes.Clear();
            nextId = 1;
            nextSeedOrder = 0;
            pineDays = 0f;
            PineLifecycleSpeed = 1f;
            BushCount = fruitBushCount = PineSeeds = PinesLost = 0;
            totals = default;
            visualsDirty = false;
            if (habitat.IsCreated) habitat.Dispose();
            if (rules.IsCreated) rules.Dispose();
            if (plantEvents.IsCreated) plantEvents.Dispose();
            if (readySeeds.IsCreated) readySeeds.Dispose();
            if (chunkTotals.IsCreated) chunkTotals.Dispose();
        }

        private void OnDestroy() => DisposeRuntime();
    }
}
