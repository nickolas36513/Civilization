using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Civilization.Ecology
{
    internal struct EcosystemOwner : ISharedComponentData, IEquatable<EcosystemOwner>
    {
        internal Entity Value;
        public bool Equals(EcosystemOwner other) => Value.Equals(other.Value);
        public override int GetHashCode() => Value.GetHashCode();
    }

    internal struct PlantState : IComponentData
    {
        internal int Id, Cell, Profile, Lifespan, Servings;
        internal float Age, Growth;
        internal uint RandomState;
        internal bool IsTree, Leafless;
    }

    internal struct PlantSeed : IComponentData
    {
        internal int Order, Cell, Profile;
        internal float ReadyDay;
        internal bool IsTree;
    }

    internal struct PlantRules
    {
        internal float DailyGrowth, LeaflessDays;
        internal int MaturityDays, ShedSeason, FruitServings, MinimumDormancy, MaximumDormancy;
        internal bool HasLeaflessModel;

        internal PlantRules(PlantProfile profile)
        {
            DailyGrowth = profile.DailyGrowth;
            LeaflessDays = profile.LeaflessDaysBeforeDeath;
            MaturityDays = profile.MaturityDays;
            ShedSeason = profile.ShedSeason;
            FruitServings = profile.FruitServings;
            MinimumDormancy = profile.MinimumSeedDormancy;
            MaximumDormancy = profile.MaximumSeedDormancy;
            HasLeaflessModel = profile.LeaflessPrefab != null;
        }
    }

    internal struct PlantTotals
    {
        internal int Fruit, Saplings, Young, Mature, Old, ConeBearing, Leafless;

        internal void Count(in PlantState plant, in PlantRules rules, int season)
        {
            if (!plant.IsTree)
            {
                Fruit += plant.Servings;
                return;
            }

            float maturity = PlantLifecycle.Maturity(plant, rules);
            if (plant.Age < maturity * 0.25f) Saplings++;
            else if (plant.Age < maturity) Young++;
            else if (plant.Age < plant.Lifespan * 0.8f) Mature++;
            else Old++;
            if (PlantLifecycle.BearsCones(plant, rules, season)) ConeBearing++;
            if (plant.Leafless) Leafless++;
        }

        internal void Add(in PlantTotals other)
        {
            Fruit += other.Fruit;
            Saplings += other.Saplings;
            Young += other.Young;
            Mature += other.Mature;
            Old += other.Old;
            ConeBearing += other.ConeBearing;
            Leafless += other.Leafless;
        }
    }

    internal static class PlantLifecycle
    {
        internal const byte SeedHabitat = 1;
        internal const byte DryTree = 2;
        internal const byte DryBush = 4;

        internal static float Maturity(in PlantState plant, in PlantRules rules) =>
            math.min(rules.MaturityDays, plant.Lifespan * 0.75f);

        internal static bool BearsCones(in PlantState plant, in PlantRules rules, int season) =>
            season == 2 && plant.Growth >= 0.65f && plant.Age >= Maturity(plant, rules)
            && plant.Age < plant.Lifespan * 0.8f;

        internal static bool IsDry(in PlantState plant, byte habitat, int season) =>
            season == 1 && (habitat & (plant.IsTree ? DryTree : DryBush)) != 0;

        internal static void RefreshSeason(ref PlantState plant, in PlantRules rules, int season, bool dry)
        {
            plant.Servings = !plant.IsTree && plant.Growth >= 0.85f && (season == 1 || season == 2)
                ? (dry ? (rules.FruitServings + 1) / 2 : rules.FruitServings) : 0;
            plant.Leafless = plant.IsTree && rules.HasLeaflessModel && (rules.ShedSeason == season
                || (rules.LeaflessDays > 0f && plant.Lifespan - plant.Age <= rules.LeaflessDays));
        }
    }

    internal enum PlantEventKind : byte { Seed, Death, Runner }

    internal struct PlantEvent : IComparable<PlantEvent>
    {
        internal Entity Entity;
        internal int PlantId;
        internal PlantEventKind Kind;
        internal PlantSeed Seed;
        public int CompareTo(PlantEvent other) => PlantId.CompareTo(other.PlantId);
    }

    [BurstCompile]
    internal struct AdvancePlantsJob : IJobChunk
    {
        internal ComponentTypeHandle<PlantState> Plants;
        [ReadOnly] internal EntityTypeHandle Entities;
        [ReadOnly] internal NativeArray<PlantRules> Profiles;
        [ReadOnly] internal NativeArray<byte> Habitat;
        [ReadOnly] internal NativeArray<CellOccupancy>.ReadOnly Occupancy;
        [NativeDisableParallelForRestriction] internal NativeArray<PlantTotals> ChunkTotals;
        internal NativeList<PlantEvent>.ParallelWriter Events;
        internal int Day, Season, GridSize;
        internal float TreeDays, TreeSpeed, GrowthRate;
        internal bool GrowBushRunners;

        public void Execute(in ArchetypeChunk chunk, int chunkIndex, bool useEnabledMask, in v128 enabledMask)
        {
            var plants = chunk.GetNativeArray(ref Plants);
            var entities = chunk.GetNativeArray(Entities);
            PlantTotals totals = default;
            for (int i = 0; i < chunk.Count; i++)
            {
                PlantState plant = plants[i];
                PlantRules rules = Profiles[plant.Profile];
                float rate = plant.IsTree ? TreeSpeed : 1f;
                bool dry = PlantLifecycle.IsDry(plant, Habitat[plant.Cell], Season);
                plant.Age += rate * (dry ? (plant.IsTree ? 1.25f : 2f) : 1f);
                if (plant.Age >= plant.Lifespan)
                {
                    plants[i] = plant;
                    Events.AddNoResize(new PlantEvent { Entity = entities[i], PlantId = plant.Id, Kind = PlantEventKind.Death });
                    continue;
                }

                plant.Growth = math.min(1f, plant.Growth + GrowthRate * rules.DailyGrowth * rate * (dry ? 0.35f : 1f));
                bool runner = GrowBushRunners && !plant.IsTree && plant.Growth >= 0.85f;
                bool producesSeed = Season == 2 && rate > 0f
                    && (plant.IsTree ? PlantLifecycle.BearsCones(plant, rules, Season) : plant.Growth >= 0.85f);
                if ((runner || producesSeed) && TryDisperse(ref plant, rules, runner, out PlantSeed seed))
                    Events.AddNoResize(new PlantEvent { PlantId = plant.Id, Seed = seed,
                        Kind = runner ? PlantEventKind.Runner : PlantEventKind.Seed });

                PlantLifecycle.RefreshSeason(ref plant, rules, Season, dry);
                plants[i] = plant;
                totals.Count(plant, rules, Season);
            }
            ChunkTotals[chunkIndex] = totals;
        }

        private bool TryDisperse(ref PlantState plant, in PlantRules rules, bool runner, out PlantSeed seed)
        {
            var random = new Unity.Mathematics.Random { state = plant.RandomState };
            int x = plant.Cell % GridSize;
            int z = plant.Cell / GridSize;
            int attempts = runner ? 9 : plant.IsTree ? 1 : 4;
            int firstNeighbor = runner ? random.NextInt(0, 9) : 0;
            for (int i = 0; i < attempts; i++)
            {
                int neighbor = (firstNeighbor + i) % 9;
                int nextX = x + (runner ? neighbor % 3 - 1 : random.NextInt(-5, 6));
                int nextZ = z + (runner ? neighbor / 3 - 1 : random.NextInt(-5, 6));
                if ((uint)nextX >= (uint)GridSize || (uint)nextZ >= (uint)GridSize)
                    continue;
                int cell = nextZ * GridSize + nextX;
                if (Occupancy[cell] != CellOccupancy.Empty || (!runner && (Habitat[cell] & PlantLifecycle.SeedHabitat) == 0))
                    continue;

                int dormancy = runner ? 0 : random.NextInt(rules.MinimumDormancy, rules.MaximumDormancy + 1);
                seed = new PlantSeed { Cell = cell, Profile = plant.Profile, IsTree = plant.IsTree,
                    ReadyDay = (plant.IsTree ? TreeDays : Day) + dormancy };
                plant.RandomState = random.state;
                return true;
            }

            plant.RandomState = random.state;
            seed = default;
            return false;
        }
    }

    internal struct ReadySeed : IComparable<ReadySeed>
    {
        internal Entity Entity;
        internal PlantSeed Seed;
        public int CompareTo(ReadySeed other)
        {
            int speciesOrder = Seed.IsTree.CompareTo(other.Seed.IsTree);
            return speciesOrder != 0 ? speciesOrder : other.Seed.Order.CompareTo(Seed.Order);
        }
    }

    [BurstCompile]
    internal struct FindReadySeedsJob : IJobChunk
    {
        [ReadOnly] internal ComponentTypeHandle<PlantSeed> Seeds;
        [ReadOnly] internal EntityTypeHandle Entities;
        internal NativeList<ReadySeed>.ParallelWriter Ready;
        internal int Day;
        internal float TreeDays;
        internal bool TreeGrowthEnabled;

        public void Execute(in ArchetypeChunk chunk, int chunkIndex, bool useEnabledMask, in v128 enabledMask)
        {
            var seeds = chunk.GetNativeArray(ref Seeds);
            var entities = chunk.GetNativeArray(Entities);
            for (int i = 0; i < chunk.Count; i++)
            {
                PlantSeed seed = seeds[i];
                if (seed.IsTree ? TreeGrowthEnabled && seed.ReadyDay <= TreeDays : seed.ReadyDay <= Day)
                    Ready.AddNoResize(new ReadySeed { Entity = entities[i], Seed = seed });
            }
        }
    }
}
