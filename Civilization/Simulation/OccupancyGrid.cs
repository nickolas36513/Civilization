using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Civilization
{
    internal enum CellOccupancy : byte { Empty, Pine, Bush, Hut, Well, PlayerBuilding }

    internal sealed class OccupancyGrid : System.IDisposable
    {
        private NativeArray<CellOccupancy> cells;
        private NativeArray<int> nearbyIndices;
        private readonly Vector2 origin;

        internal int Size { get; }
        internal int OccupiedCount { get; private set; }
        internal int CellCount => cells.Length;
        internal NativeArray<CellOccupancy>.ReadOnly Cells => cells.AsReadOnly();

        internal OccupancyGrid(int size)
        {
            Size = size;
            origin = new Vector2(-size / 2f, -size / 2f);
            cells = new NativeArray<CellOccupancy>(size * size, Allocator.Persistent);
        }

        internal Vector2Int WorldToCell(Vector2 position)
        {
            Vector2 local = position - origin;
            return new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
        }

        internal Vector2 CellCenter(Vector2Int cell) => origin + new Vector2(cell.x + 0.5f, cell.y + 0.5f);

        // The returned view is consumed on the main thread before the next query.
        internal NativeArray<int> FindNearbyCells(Vector2 position, float radius, CellOccupancy occupancy)
        {
            if (radius < 0f)
                return default;

            var extent = new Vector2(radius, radius);
            Vector2Int minimum = WorldToCell(position - extent);
            Vector2Int maximum = WorldToCell(position + extent);
            int left = Mathf.Max(0, minimum.x);
            int bottom = Mathf.Max(0, minimum.y);
            int right = Mathf.Min(Size - 1, maximum.x);
            int top = Mathf.Min(Size - 1, maximum.y);
            if (right < left || top < bottom)
                return default;

            int capacity = (right - left + 1) * (top - bottom + 1);
            if (!nearbyIndices.IsCreated || nearbyIndices.Length < capacity)
            {
                if (nearbyIndices.IsCreated)
                    nearbyIndices.Dispose();
                nearbyIndices = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            int count = OccupancyScan.Find(in cells, ref nearbyIndices, Size, left, bottom, right, top,
                origin.x, origin.y, position.x, position.y, radius * radius, occupancy);
            return nearbyIndices.GetSubArray(0, count);
        }

        internal Vector2Int CellFromIndex(int index) => new(index % Size, index / Size);

        public void Dispose()
        {
            if (nearbyIndices.IsCreated)
                nearbyIndices.Dispose();
            if (cells.IsCreated)
                cells.Dispose();
        }

        internal RectInt FootprintAt(Vector2 center, Vector2 dimensions)
        {
            if (dimensions.x <= 0f || dimensions.y <= 0f)
                return default;

            Vector2 min = center - dimensions * 0.5f - origin;
            Vector2 max = center + dimensions * 0.5f - origin;
            int x = Mathf.FloorToInt(min.x);
            int y = Mathf.FloorToInt(min.y);
            return new RectInt(x, y, Mathf.CeilToInt(max.x) - x, Mathf.CeilToInt(max.y) - y);
        }

        internal static RectInt SingleCell(Vector2Int cell) => new(cell.x, cell.y, 1, 1);

        internal bool Contains(Vector2Int cell) => cell.x >= 0 && cell.x < Size && cell.y >= 0 && cell.y < Size;

        internal bool TryGetOccupancy(Vector2Int cell, out CellOccupancy occupancy)
        {
            bool inside = Contains(cell);
            occupancy = inside ? cells[cell.y * Size + cell.x] : CellOccupancy.Empty;
            return inside;
        }

        internal bool IsAvailable(RectInt footprint) => Matches(footprint, CellOccupancy.Empty);

        internal bool TryOccupy(RectInt footprint, CellOccupancy occupancy)
        {
            if (occupancy == CellOccupancy.Empty || !IsAvailable(footprint))
                return false;

            Fill(footprint, occupancy);
            OccupiedCount += footprint.width * footprint.height;
            return true;
        }

        internal bool Release(RectInt footprint, CellOccupancy occupancy)
        {
            if (occupancy == CellOccupancy.Empty || !Matches(footprint, occupancy))
                return false;

            Fill(footprint, CellOccupancy.Empty);
            OccupiedCount -= footprint.width * footprint.height;
            return true;
        }

        private bool Matches(RectInt footprint, CellOccupancy occupancy)
        {
            if (footprint.width <= 0 || footprint.height <= 0
                || footprint.xMin < 0 || footprint.yMin < 0 || footprint.xMax > Size || footprint.yMax > Size)
                return false;

            for (int y = footprint.yMin; y < footprint.yMax; y++)
                for (int x = footprint.xMin; x < footprint.xMax; x++)
                    if (cells[y * Size + x] != occupancy)
                        return false;

            return true;
        }

        private void Fill(RectInt footprint, CellOccupancy occupancy)
        {
            for (int y = footprint.yMin; y < footprint.yMax; y++)
                for (int x = footprint.xMin; x < footprint.xMax; x++)
                    cells[y * Size + x] = occupancy;
        }
    }

    [BurstCompile]
    internal static class OccupancyScan
    {
        [BurstCompile]
        internal static int Find(in NativeArray<CellOccupancy> cells, ref NativeArray<int> results,
            int size, int left, int bottom, int right, int top, float originX, float originY,
            float positionX, float positionY, float radiusSquared, CellOccupancy occupancy)
        {
            int count = 0;
            for (int y = bottom; y <= top; y++)
            {
                float dy = originY + (y + 0.5f) - positionY;
                float dySquared = dy * dy;
                int row = y * size;
                for (int x = left; x <= right; x++)
                {
                    int index = row + x;
                    if (cells[index] != occupancy)
                        continue;

                    float dx = originX + (x + 0.5f) - positionX;
                    if (dx * dx + dySquared <= radiusSquared)
                        results[count++] = index;
                }
            }
            return count;
        }
    }
}
