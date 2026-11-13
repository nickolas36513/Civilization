using System.Collections.Generic;
using UnityEngine;

namespace Civilization.Ecology
{
    internal sealed class ProceduralWorldGenerator
    {
        private readonly OccupancyGrid grid;
        private readonly System.Random random;
        private readonly Vector2 habitatOffset;
        private readonly float habitatScale;

        internal ProceduralWorldGenerator(OccupancyGrid grid, int seed, float habitatScale)
        {
            this.grid = grid;
            this.habitatScale = Mathf.Clamp(habitatScale, 0.01f, 0.25f);
            random = new System.Random(seed ^ 0x46C651A7);

            // Mix the full signed seed and keep the coordinates small enough for Perlin noise precision.
            uint hash = unchecked((uint)seed * 0x9E3779B9u);
            habitatOffset = new Vector2((hash & 0xFFFFu) / 64f, (hash >> 16) / 64f);
        }

        internal float Habitat(Vector2Int cell)
        {
            Vector2 point = grid.CellCenter(cell) * habitatScale + habitatOffset;
            return Mathf.PerlinNoise(point.x, point.y);
        }

        internal IEnumerable<(Vector2Int Cell, bool IsTree)> MixedPlants(int spacing, float density, float treeChance)
        {
            density = Mathf.Clamp01(density);
            if (density == 0f)
                yield break;

            treeChance = Mathf.Clamp01(treeChance);
            foreach (Vector2Int cell in SampleCells(Mathf.Clamp(spacing, 1, 12)))
            {
                float habitat = Habitat(cell);
                if (habitat > 0.45f && random.NextDouble() < (habitat - 0.40f) * density)
                    yield return (cell, random.NextDouble() < treeChance);
            }
        }

        internal IEnumerable<Vector2Int> OuterTrees(float clearance)
        {
            clearance = Mathf.Clamp(clearance, 0f, grid.Size * 0.5f);
            foreach (Vector2Int cell in SampleCells(4))
            {
                Vector2 point = grid.CellCenter(cell);
                if (Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y)) >= clearance)
                    yield return cell;
            }
        }

        private IEnumerable<Vector2Int> SampleCells(int spacing)
        {
            int width = (grid.Size + spacing - 1) / spacing;
            int count = width * width;
            int tile = random.Next(count);
            int stride = random.Next(1, count);
            while (GreatestCommonDivisor(stride, count) != 1)
                stride++;

            // A coprime stride visits every tile once without storing or shuffling the grid.
            for (int i = 0; i < count; i++)
            {
                int x = tile % width * spacing;
                int z = tile / width * spacing;
                yield return new Vector2Int(x + random.Next(Mathf.Min(spacing, grid.Size - x)),
                    z + random.Next(Mathf.Min(spacing, grid.Size - z)));
                tile = (tile + stride) % count;
            }
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
                (a, b) = (b, a % b);
            return a;
        }
    }
}
