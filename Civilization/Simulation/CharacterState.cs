using System.Collections.Generic;
using Civilization.Ecology;
using UnityEngine;

namespace Civilization
{
    internal enum ResidentGender { Male, Female }

    internal sealed class CharacterState
    {
        internal const float Step = 0.1f;
        internal const float DayLength = 120f;


        internal const float PerceptionRange = 5f;
        private const float Critical = 0.8f;
        internal const float SearchSpacing = 6.4f;

        private readonly TrialWorld world;
        internal int Id { get; }
        internal string Name { get; }
        internal ResidentGender Gender { get; }
        internal float Age;
        internal bool IsAdult => Age >= PopulationSystem.AdultAge;
        internal int FirstParent { get; }
        internal int SecondParent { get; }
        internal HouseholdSystem.Hut Hut;
        internal HouseholdSystem.Hut ChildhoodHut;
        internal CharacterState Spouse;
        internal float Loneliness = 0.55f;
        internal float MatingDrive = 0.25f;
        internal float EarliestMatingTime;
        internal CharacterState SocialPartner;
        internal CharacterState LastSocialPartner;
        internal Vector2? SocialTarget;
        private readonly Dictionary<int, ResourceObservation> observations = new();
        private bool preservedFruit;
        internal bool HasPreservedFruit => preservedFruit;
        private Vector2 Home => Hut?.Position ?? Position;

        private struct ResourceObservation
        {
            public int Tick;
            public Vector2 Position;
            public int RetryDay;
            public bool Gone;
        }

        internal int DiscoveredCount { get; private set; }
        internal int ReportsReceived { get; private set; }
        internal bool CanSocialize => !world.IsNight
            && Hunger < SearchThreshold(false) && Thirst < SearchThreshold(true);

        internal DeathMemory Memory { get; private set; }
        internal Vector2 Position { get; private set; }
        internal Vector2 PreviousPosition { get; private set; }
        internal Vector2 TravelDirection { get; private set; }
        internal float Hunger { get; private set; }
        internal float Thirst { get; private set; }
        internal float FoodTimeLeft => Mathf.Max(0f, (1f - Hunger) * world.DaysWithoutFood);
        internal float WaterTimeLeft => Mathf.Max(0f, (1f - Thirst) * world.DaysWithoutWater);
        internal bool KnowsWell => observations.ContainsKey(0);
        internal float Motivation { get; private set; }
        internal float Stress { get; private set; }
        internal bool Emergency => Mathf.Max(Hunger, Thirst) >= Critical;
        internal bool FightOrFlight => Emergency && emergencyAge < 4f;


        internal string Action { get; private set; }
        internal string Reason { get; private set; }
        internal float ActionAge { get; private set; }
        internal double DecisionMs { get; private set; }

        private float emergencyAge;
        private int explorationWaypoint;
        private static readonly double MillisecondsPerTimestamp = 1000d / System.Diagnostics.Stopwatch.Frequency;
        internal bool IsDead { get; private set; }

        internal CharacterState(TrialWorld world, int id, float age, HouseholdSystem.Hut hut, int firstParent, int secondParent)
        {
            this.world = world;
            Id = id;
            Name = NameLibrary.ForId(id);
            Gender = id % 2 == 0 ? ResidentGender.Male : ResidentGender.Female;
            Age = age;
            FirstParent = firstParent;
            SecondParent = secondParent;
            world.Housing.MoveIn(this, hut);

            Memory = DeathMemoryStore.Load(id);
            Reset();
        }

        internal void Reset()
        {
            observations.Clear();
            IsDead = false;
            preservedFruit = false;
            DiscoveredCount = ReportsReceived = 0;
            PreviousPosition = Position = Home;
            TravelDirection = Vector2.zero;
            Hunger = Thirst = Stress = 0f;
            Motivation = 0.75f;
            ActionAge = emergencyAge = 0;
            int width = world.SearchGridWidth;
            int middle = width / 2;
            explorationWaypoint = Id switch
            {
                0 => (middle - 1) * width + middle - 1,
                1 => middle * width + middle,
                _ => (Id * 36) % (width * width)
            };
            Action = "Starting";
            Reason = "At the hut";
        }

        internal float SearchThreshold(bool water)
        {
            int deaths = water ? Memory.ThirstDeaths : Memory.HungerDeaths;
            return Mathf.Max(0.2f, 0.48f - 0.08f * deaths);
        }

        internal bool Tick()
        {
            PreviousPosition = Position;

            Hunger += Step / (world.DaysWithoutFood * DayLength);
            Thirst += Step / (world.DaysWithoutWater * DayLength);
            bool waterMoreUrgent = WaterTimeLeft <= FoodTimeLeft;
            if (Hunger >= 1f || Thirst >= 1f)
            {
                bool thirst = Thirst >= 1f && (Hunger < 1f || waterMoreUrgent);
                RecordDeath(thirst ? "Thirst" : "Hunger");
                return false;
            }

            foreach (Ecosystem.Bush bush in world.Ecology.FindNearbyBushes(Position, PerceptionRange))
                Observe(bush.Id, bush.Position, bush.Servings > 0);
            if ((Position - TrialWorld.WellPosition).sqrMagnitude <= PerceptionRange * PerceptionRange)
                Observe(0, TrialWorld.WellPosition, true);

            emergencyAge = Emergency ? emergencyAge + Step : 0f;
            long decisionStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Decide(waterMoreUrgent, SocialTarget);
            DecisionMs = (System.Diagnostics.Stopwatch.GetTimestamp() - decisionStart) * MillisecondsPerTimestamp;

            bool sleeping = Action == "Sleeping";
            float stressTarget = sleeping ? 0f : Mathf.InverseLerp(0.4f, 1f, Mathf.Max(Hunger, Thirst));
            Stress = Mathf.MoveTowards(Stress, stressTarget, Step * (sleeping ? 0.035f : 0.08f));
            return true;
        }

        internal void RecordDeath(string cause)
        {
            IsDead = true;
            DeathMemory memory = Memory;
            if (cause == "Thirst") memory.ThirstDeaths++;
            if (cause == "Hunger") memory.HungerDeaths++;
            memory.Cause = cause;
            memory.Action = Action;
            memory.HadKnownSupply = cause != "Old age" && FindSource(cause == "Thirst") >= 0;
            Memory = memory;
            DeathMemoryStore.Save(Id, memory);
        }

        private int FindSource(bool water)
        {
            if (water)
                return observations.ContainsKey(0) ? 0 : -1;

            if (!world.Ecology.IsFruitSeason)
                return -1;

            int nearest = -1;
            float distance = float.PositiveInfinity;
            foreach (var entry in observations)
            {
                ResourceObservation report = entry.Value;
                float candidate = (Position - report.Position).sqrMagnitude;
                if (entry.Key == 0 || report.Gone || report.RetryDay > world.Day || candidate >= distance)
                    continue;
                nearest = entry.Key;
                distance = candidate;
            }
            return nearest;
        }

        private void Decide(bool waterMoreUrgent, Vector2? socialTarget)
        {
            bool needsWater = Thirst >= SearchThreshold(true);
            bool needsFood = Hunger >= SearchThreshold(false);
            bool water = needsWater && (!needsFood || waterMoreUrgent);
            if (Emergency)
                water = Thirst >= Critical && (Hunger < Critical || waterMoreUrgent);
            else if ((Action == "Seeking water" || Action == "Searching for water") && needsWater)
                water = true;
            else if ((Action == "Seeking food" || Action == "Searching for food") && needsFood)
                water = false;

            bool night = world.IsNight;
            bool tooDrivenToSleep = Hunger > 0.5f && Thirst > 0.5f;
            if (tooDrivenToSleep)
                Motivation = Mathf.Max(Motivation, 0.85f);

            if ((needsWater || needsFood) && (!night || Emergency || tooDrivenToSleep))
            {
                string seeking = water ? "Seeking water" : "Seeking food";
                int index = FindSource(water);
                if (index < 0)
                {
                    if (!water && preservedFruit)
                    {
                        preservedFruit = false;
                        Hunger = 0f;
                        SetAction("Eating", "Using a saved fruit portion");
                        return;
                    }
                    Explore(water ? "Searching for water" : "Searching for food", "No usable source discovered");
                    return;
                }

                Vector2 destination = observations[index].Position;
                string reason = Emergency ? "Survival emergency" : "Need threshold reached";
                if (night && tooDrivenToSleep && !Emergency)
                    reason = "Too hungry and thirsty to sleep";
                SetAction(seeking, reason);
                Move(destination);
                if (Position != destination)
                    return;
                if (water)
                    Thirst = 0f;
                else if (world.Ecology.TryReadBush(destination, index, out Ecosystem.Bush bush))
                {
                    if (world.Ecology.TryHarvest(bush))
                    {
                        Hunger = 0f;
                        if (!preservedFruit && world.Ecology.AvailableFruit > world.Population.HungryResidents)
                            preservedFruit = world.Ecology.TryHarvest(bush);
                    }
                    world.Ecology.TryReadBush(destination, index, out bush);
                    Observe(index, destination, bush.Servings > 0);
                }
                else
                {
                    Observe(index, destination, false, gone: true);
                }
                return;
            }

            if (night)
            {
                Move(Home);
                SetAction(Position == Home ? "Sleeping" : "Going home", Hut == null ? "Resting outdoors" : "Nighttime");
                if (Position == Home)
                    Motivation = Mathf.MoveTowards(Motivation, 1f, Step * 0.15f);
            }
            else if (socialTarget.HasValue)
            {
                Vector2 target = socialTarget.Value;
                bool close = (Position - target).sqrMagnitude <= SocialTrial.TalkRangeSquared;
                SetAction(close ? "Socializing" : "Approaching partner", Spouse == SocialPartner ? "Spending time with spouse" : "Seeking company");
                if (!close)
                    Move(target);
            }
            else if (!IsAdult && Hut != null)
            {
                Move(Home + new Vector2(0, -0.9f - 0.35f * (Id % 3)));
                SetAction("Playing", "Staying close to home");
            }
            else if (Motivation < 0.3f || (Action == "Resting" && Motivation < 0.75f))
            {
                SetAction("Resting", "Recovering motivation");
                Motivation = Mathf.MoveTowards(Motivation, 1f, Step * 0.12f);
            }
            else
            {
                if (IsAdult && Spouse == null)
                    Explore("Seeking mate", "Looking for a compatible adult");
                else
                    Explore("Wandering", "Needs are stable");
                Motivation = Mathf.MoveTowards(Motivation, 0f, Step * 0.025f);
            }
        }

        private void Explore(string action, string reason)
        {
            SetAction(action, reason);
            // Sweep alternating rows. A waypoint is retained through meals and sleep.
            int width = world.SearchGridWidth;
            int row = explorationWaypoint / width;
            int column = explorationWaypoint % width;
            if (row % 2 != (width / 2) % 2)
                column = width - 1 - column;

            float center = (width - 1) * 0.5f;
            Vector2 target = new Vector2(column - center, row - center) * SearchSpacing;
            Move(target);
            if (Position == target)
                explorationWaypoint = (explorationWaypoint + 1) % (width * width);
        }

        private void Move(Vector2 destination)
        {
            // One burst per continuous emergency.
            float speed = FightOrFlight ? 4.5f : 2.5f;
            Vector2 next = Vector2.MoveTowards(Position, destination, Step * speed);
            Vector2 movement = next - Position;
            if (movement.sqrMagnitude > 0.000001f)
                TravelDirection = movement;
            Position = next;
        }

        private void SetAction(string action, string reason)
        {
            ActionAge = Action == action ? ActionAge + Step : 0f;
            Action = action;
            Reason = reason;
        }

        private void Observe(int sourceId, Vector2 position, bool available, bool gone = false)
        {
            observations[sourceId] = new ResourceObservation
            {
                Tick = world.TickNumber,
                Position = position,
                RetryDay = available ? world.Day : world.Day + 1,
                Gone = gone
            };
            DiscoveredCount = observations.Count;
        }

        internal int ShareDiscoveriesWith(CharacterState partner)
        {
            int shared = 0;
            foreach (var entry in observations)
            {
                bool known = partner.observations.TryGetValue(entry.Key, out ResourceObservation previous);
                if (known && entry.Value.Tick <= previous.Tick)
                    continue;
                if (!known)
                {
                    partner.ReportsReceived++;
                    shared++;
                }
                partner.observations[entry.Key] = entry.Value;
            }
            partner.DiscoveredCount = partner.observations.Count;
            return shared;
        }
    }
}
