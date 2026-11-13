using System.Collections.Generic;
using Civilization.Ecology;
using UnityEngine;

namespace Civilization
{
    internal sealed class TrialWorld : System.IDisposable
    {
        internal static readonly Vector2 WellPosition = new(10, 8);
        internal Ecosystem Ecology { get; }
        internal List<CharacterState> People { get; } = new();
        internal OccupancyGrid Grid { get; }
        internal int SearchGridWidth { get; }
        internal HouseholdSystem Housing { get; }
        internal PopulationSystem Population { get; }
        internal int Generation { get; private set; }
        private const int EventLimit = 120;
        private readonly Queue<string> recentEvents = new();
        private int nextId;
        internal IEnumerable<string> RecentEvents => recentEvents;
        internal int EventVersion { get; private set; }
        internal float DaysWithoutFood { get; set; } = 3f;
        internal float DaysWithoutWater { get; set; } = 2f;
        internal SocialTrial Social { get; } = new();
        internal int TickNumber { get; private set; }
        internal float TimeAlive => TickNumber * CharacterState.Step;
        internal float Hour => Mathf.Repeat(8f + 24f * TimeAlive / CharacterState.DayLength, 24f);
        internal int Day => Mathf.FloorToInt((TimeAlive + CharacterState.DayLength / 3f) / CharacterState.DayLength);
        internal bool IsNight => Hour >= 21f || Hour < 6f;



        internal TrialWorld(Ecosystem ecology)
        {
            Ecology = ecology;
            Grid = new OccupancyGrid(ecology.WorldSize);
            SearchGridWidth = Mathf.CeilToInt(Grid.Size / CharacterState.SearchSpacing);
            try
            {
                Grid.TryOccupy(Grid.FootprintAt(WellPosition, new Vector2(3f, 3f)), CellOccupancy.Well);
                Housing = new HouseholdSystem(Grid);
                Population = new PopulationSystem(this);
                Reset();
            }
            catch
            {
                Grid.Dispose();
                throw;
            }
        }

        private void Reset()
        {
            Generation++;
            nextId = 0;
            People.Clear();
            Housing.Reset();
            Population.Reset();
            AddPerson(21f, Housing.Huts[0]);
            AddPerson(21f, Housing.Huts[1]);
            Social.Reset();
            RecordEvent(Generation == 1 ? "The settlement begins" : $"Settlement restarted · trial {Generation}");
        }

        internal void RecordEvent(string description)
        {
            if (recentEvents.Count == EventLimit)
                recentEvents.Dequeue();
            int minutes = (int)(Hour * 60);
            recentEvents.Enqueue($"Day {Day + 1} {minutes / 60:00}:{minutes % 60:00}  ·  {description}");
            EventVersion++;
        }

        internal CharacterState AddPerson(float age, HouseholdSystem.Hut hut, int firstParent = -1, int secondParent = -1)
        {
            var person = new CharacterState(this, nextId++, age, hut, firstParent, secondParent);
            People.Add(person);
            return person;
        }

        internal void Tick()
        {
            int previousDay = Day;
            TickNumber++;
            if (Day != previousDay)
                Ecology.AdvanceDay();
            Social.Plan(this);
            for (int i = People.Count - 1; i >= 0; i--)
            {
                CharacterState person = People[i];
                if (!person.Tick())
                    Population.Remove(person);
            }
            if (TickNumber % 10 == 0)
            {
                Population.SampleFoodPressure();
                Social.TeachChildren(this);
            }
            Social.Resolve(this);
            Population.Tick();

            if (People.Count == 0)
                Reset();
        }

        public void Dispose() => Grid.Dispose();
    }
}
