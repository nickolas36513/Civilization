using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Civilization
{
    [Serializable]
    internal sealed class BuildingDefinition
    {
        [SerializeField] private string label;
        [SerializeField] private Texture2D icon;
        [SerializeField] private GameObject prefab;
        [Tooltip("Footprint in grid cells. The prefab pivot belongs at the center of the footprint, at ground level.")]
        [SerializeField] private Vector2Int footprint = Vector2Int.one;

        internal string Label => string.IsNullOrWhiteSpace(label) ? prefab.name : label;
        internal Texture2D Icon => icon;
        internal GameObject Prefab => prefab;
        internal Vector2Int Footprint => new(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
    }

    internal sealed class BuildingPlacement : IDisposable
    {
        private static readonly Plane Ground = new(Vector3.up, Vector3.zero);
        private static readonly Color FreeColor = new(0.74f, 0.82f, 0.84f, 0.38f);
        private static readonly Color BlockedColor = new(1f, 0.20f, 0.17f, 0.55f);
        private static readonly int Tint = Shader.PropertyToID("_Tint");

        private readonly OccupancyGrid grid;
        private readonly Camera camera;
        private readonly Transform parent;
        private readonly List<PlacedBuilding> placed = new();
        private BuildingDefinition selected;
        private GameObject preview;
        private Material previewMaterial;
        private int quarterTurns;

        private readonly struct PlacedBuilding
        {
            internal readonly GameObject Model;
            internal readonly RectInt Footprint;
            internal readonly Bounds Bounds;

            internal PlacedBuilding(GameObject model, RectInt footprint, Bounds bounds)
            {
                Model = model;
                Footprint = footprint;
                Bounds = bounds;
            }
        }

        internal BuildingDefinition Selected => selected;
        internal int RotationDegrees => quarterTurns * 90;
        internal string Status { get; private set; } = "Select a building";

        internal BuildingPlacement(OccupancyGrid grid, Camera camera, Transform parent)
        {
            this.grid = grid;
            this.camera = camera;
            this.parent = parent;
        }

        internal void Select(BuildingDefinition building)
        {
            CancelPreview();
            if (building?.Prefab == null)
                return;

            if (previewMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("BuildingPreview");
                if (shader == null || !shader.isSupported)
                {
                    Status = "Preview shader unavailable";
                    return;
                }
                previewMaterial = new Material(shader);
            }

            selected = building;
            preview = new GameObject("Building preview");
            preview.transform.SetParent(parent, false);
            Transform source = building.Prefab.transform;
            CopyPreviewMeshes(source, preview.transform, Vector3.zero, Quaternion.identity);
            preview.SetActive(false);
            Status = "Point at the map";
        }

        internal void Update(TrialHud hud)
        {
            if (camera == null || Mouse.current == null)
                return;

            Vector2 pointer = Mouse.current.position.ReadValue();
            bool overMap = camera.pixelRect.Contains(pointer) && !hud.BlocksCamera(pointer);
            Ray ray = overMap ? camera.ScreenPointToRay(pointer) : default;
            Keyboard keyboard = hud.IsEditingText ? null : Keyboard.current;
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame)
            {
                if (overMap && TryRemove(ray))
                    Status = "Building removed";
                else if (selected != null)
                    CancelPreview();
                return;
            }

            if (selected == null)
                return;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                quarterTurns = (quarterTurns + 1) % 4;

            if (!overMap || !Ground.Raycast(ray, out float distance))
            {
                if (preview.activeSelf) preview.SetActive(false);
                Status = "Point at the map";
                return;
            }

            Vector3 hit = ray.GetPoint(distance);
            Vector2Int footprintSize = selected.Footprint;
            if ((quarterTurns & 1) != 0)
                footprintSize = new Vector2Int(footprintSize.y, footprintSize.x);

            RectInt footprint = SnapFootprint(grid, new Vector2(hit.x, hit.z), footprintSize, out Vector2 center);
            var position = new Vector3(center.x, 0f, center.y);
            Quaternion rotation = Quaternion.Euler(0f, RotationDegrees, 0f);
            preview.transform.SetPositionAndRotation(position, rotation);
            if (!preview.activeSelf) preview.SetActive(true);

            bool available = grid.IsAvailable(footprint);
            previewMaterial.SetColor(Tint, available ? FreeColor : BlockedColor);
            bool inBounds = grid.Contains(footprint.min) && grid.Contains(footprint.max - Vector2Int.one);
            Status = available ? "Ready to place" : inBounds ? "Footprint occupied" : "Outside the map";

            if (available && keyboard != null && keyboard.eKey.wasPressedThisFrame)
                Place(footprint, position, rotation);
        }

        internal static RectInt SnapFootprint(OccupancyGrid grid, Vector2 point, Vector2Int size, out Vector2 center)
        {
            Vector2 offset = ((Vector2)size - Vector2.one) * 0.5f;
            Vector2Int start = grid.WorldToCell(point - offset);
            center = grid.CellCenter(start) + offset;
            return new RectInt(start, size);
        }

        private void Place(RectInt footprint, Vector3 position, Quaternion rotation)
        {
            if (!grid.TryOccupy(footprint, CellOccupancy.PlayerBuilding))
                return;

            GameObject model = null;
            try
            {
                model = Object.Instantiate(selected.Prefab, position, rotation, parent);
                model.name = selected.Label;
                Bounds bounds = new(position + Vector3.up * 0.5f, new Vector3(footprint.width, 1f, footprint.height));
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(renderer.bounds);
                placed.Add(new PlacedBuilding(model, footprint, bounds));
            }
            catch
            {
                grid.Release(footprint, CellOccupancy.PlayerBuilding);
                if (model != null) Object.Destroy(model);
                throw;
            }

            CancelPreview();
            Status = "Building placed";
        }

        private bool TryRemove(Ray ray)
        {
            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i].Bounds.IntersectRay(ray, out float distance) && distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }
            if (nearest < 0)
                return false;

            PlacedBuilding building = placed[nearest];
            if (!grid.Release(building.Footprint, CellOccupancy.PlayerBuilding))
                return false;
            placed.RemoveAt(nearest);
            Object.Destroy(building.Model);
            return true;
        }

        private void CopyPreviewMeshes(Transform source, Transform target, Vector3 position, Quaternion rotation)
        {
            if (!source.gameObject.activeSelf)
                return;

            var node = new GameObject(source.name);
            node.transform.SetParent(target, false);
            node.transform.SetLocalPositionAndRotation(position, rotation);
            node.transform.localScale = source.localScale;

            if (source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                && source.TryGetComponent(out MeshRenderer renderer) && renderer.enabled)
            {
                node.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var previewRenderer = node.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                Array.Fill(materials, previewMaterial);
                previewRenderer.sharedMaterials = materials;
                previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
                previewRenderer.receiveShadows = false;
            }
            foreach (Transform child in source)
                CopyPreviewMeshes(child, node.transform, child.localPosition, child.localRotation);
        }

        private void CancelPreview()
        {
            if (preview != null)
            {
                preview.SetActive(false);
                Object.Destroy(preview);
            }
            preview = null;
            selected = null;
            quarterTurns = 0;
            Status = "Select a building";
        }

        public void Dispose()
        {
            CancelPreview();
            foreach (PlacedBuilding building in placed)
            {
                grid.Release(building.Footprint, CellOccupancy.PlayerBuilding);
                Object.Destroy(building.Model);
            }
            placed.Clear();
            if (previewMaterial != null) Object.Destroy(previewMaterial);
        }
    }
}
