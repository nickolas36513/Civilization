using System.Collections.Generic;

namespace Civilization
{
    internal enum MatingAvailability { Available, NoSharedHut, ChildLimit, Expecting, Recovery }

    internal sealed class PopulationSystem
    {
        internal const float AdultAge = 18f;
        private const float RecoveryDaysAfterBirth = 0.5f;
        private float lastArrival;
        internal float YearsPerDay { get; set; } = 3f;
        internal float Lifespan { get; set; } = 72f;
        internal float PregnancyDays { get; set; } = 1f;
        internal float ArrivalIntervalDays { get; set; } = 3f;
        internal int PopulationLimit { get; set; }
        private bool CanAddResident => !FoodScarce
            && (PopulationLimit == 0 || world.People.Count + PendingBirths < PopulationLimit);
        internal int HungryResidents { get; private set; }
        internal int SavedFruit { get; private set; }
        internal bool FoodScarce { get; private set; }

        private sealed class Pregnancy
        {
            internal CharacterState Mother;
            internal CharacterState Partner;

            internal float Due;
        }

        private readonly TrialWorld world;
        private readonly List<Pregnancy> pregnancies = new();

        internal int PendingBirths => pregnancies.Count;
        internal int Births { get; private set; }
        internal int NewcomerTarget { get; set; } = 6;
        internal int Arrivals { get; private set; }
        internal int HungerDeaths { get; private set; }
        internal int ThirstDeaths { get; private set; }
        internal int Deaths { get; private set; }


        internal PopulationSystem(TrialWorld world) => this.world = world;

        internal bool IsExpecting(CharacterState person) =>
            pregnancies.Exists(pregnancy => pregnancy.Mother == person || pregnancy.Partner == person);

        internal MatingAvailability MatingStatus(CharacterState first, CharacterState second)
        {
            if (first.Hut == null || first.Hut != second.Hut)
                return MatingAvailability.NoSharedHut;
            if (first.Hut.ChildCount >= HouseholdSystem.ChildrenBeforeMatingPauses)
                return MatingAvailability.ChildLimit;
            if (IsExpecting(first) || IsExpecting(second))
                return MatingAvailability.Expecting;
            if (world.TimeAlive < first.EarliestMatingTime || world.TimeAlive < second.EarliestMatingTime)
                return MatingAvailability.Recovery;
            return MatingAvailability.Available;
        }

        internal bool TryConceive(CharacterState mother, CharacterState partner)
        {
            if (!CanAddResident || MatingStatus(mother, partner) != MatingAvailability.Available)
                return false;

            pregnancies.Add(new Pregnancy
            {
                Mother = mother, Partner = partner,
                Due = world.TimeAlive + PregnancyDays * CharacterState.DayLength
            });
            world.RecordEvent($"{mother.Name} and {partner.Name} are expecting a child");
            return true;
        }

        internal void SampleFoodPressure()
        {
            HungryResidents = SavedFruit = 0;
            int hungryWithoutReserves = 0;
            foreach (CharacterState person in world.People)
            {
                bool hungry = person.Hunger >= person.SearchThreshold(false);
                if (hungry)
                    HungryResidents++;
                if (person.HasPreservedFruit)
                    SavedFruit++;
                else if (hungry)
                    hungryWithoutReserves++;
            }

            FoodScarce = hungryWithoutReserves > world.Ecology.AvailableFruit;
        }

        internal void Tick()
        {
            for (int i = world.People.Count - 1; i >= 0; i--)
            {
                CharacterState person = world.People[i];
                bool wasAdult = person.IsAdult;
                person.Age += CharacterState.Step * YearsPerDay / CharacterState.DayLength;
                if (person.Age >= Lifespan)
                {
                    person.RecordDeath("Old age");
                    Remove(person);
                }
                else if (!wasAdult && person.IsAdult)
                {
                    person.ChildhoodHut = person.Hut;
                    world.Housing.MoveIn(person, null);
                    world.RecordEvent($"{person.Name} came of age and left home");
                }
            }

            for (int i = pregnancies.Count - 1; i >= 0; i--)
            {
                Pregnancy pregnancy = pregnancies[i];
                if (world.TimeAlive < pregnancy.Due)
                    continue;

                pregnancies.RemoveAt(i);
                pregnancy.Mother.EarliestMatingTime = world.TimeAlive + RecoveryDaysAfterBirth * CharacterState.DayLength;
                CharacterState child = world.AddPerson(0f, pregnancy.Mother.Hut, pregnancy.Mother.Id, pregnancy.Partner.Id);
                Births++;
                world.RecordEvent($"{child.Name} was born");
            }

            world.Housing.HouseHomelessAdults(world);

            AdmitNewcomer();
        }

        private void AdmitNewcomer()
        {
            if (world.People.Count == 0 || world.People.Count + PendingBirths >= NewcomerTarget
                || !CanAddResident || world.TimeAlive - lastArrival < ArrivalIntervalDays * CharacterState.DayLength)
                return;

            HouseholdSystem.Hut home = world.Housing.FindVacancy(1);
            if (home == null)
                return;

            CharacterState newcomer = world.AddPerson(21f, home);
            Arrivals++;
            lastArrival = world.TimeAlive;
            world.RecordEvent($"{newcomer.Name} arrived and moved into a hut");
        }

        internal void Remove(CharacterState person)
        {
            pregnancies.RemoveAll(pregnancy => pregnancy.Mother == person);

            if (person.Spouse != null)
                person.Spouse.Spouse = null;
            world.Social.Forget(person, world.People);
            world.Housing.MoveIn(person, null);
            world.People.Remove(person);
            Deaths++;
            if (person.Memory.Cause == "Hunger")
                HungerDeaths++;
            if (person.Memory.Cause == "Thirst")
                ThirstDeaths++;
            world.RecordEvent($"{person.Name} died · {person.Memory.Cause}");
        }

        internal void Reset()
        {
            pregnancies.Clear();
            Births = Deaths = Arrivals = HungerDeaths = ThirstDeaths = HungryResidents = SavedFruit = 0;
            FoodScarce = false;
            lastArrival = world.TimeAlive;
        }
    }
}
