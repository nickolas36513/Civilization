using System.Collections.Generic;
using UnityEngine;

namespace Civilization
{
    internal sealed class HouseholdSystem
    {
        internal const int AdultCapacity = 2;
        internal const int ChildrenBeforeMatingPauses = 2;

        internal sealed class Hut
        {
            internal readonly Vector2 Position;
            internal readonly RectInt Footprint;
            internal Vector2 Center => Position + HutCenterOffset;
            internal readonly List<CharacterState> Residents = new();

            internal int AdultCount
            {
                get
                {
                    int count = 0;
                    foreach (CharacterState resident in Residents)
                        if (resident.IsAdult)
                            count++;
                    return count;
                }
            }

            internal int ChildCount => Residents.Count - AdultCount;

            internal Hut(Vector2 position, RectInt footprint)
            {
                Position = position;
                Footprint = footprint;
            }
        }

        private static readonly Vector2[] Sites =
        {
            new(0, 0), new(4, 0), new(-4, 0), new(8, 0), new(-8, 0),
            new(0, -5), new(4, -5), new(-4, -5), new(8, -5), new(-8, -5),
            new(0, 5), new(4, 5), new(-4, 5), new(8, 5)
        };

        private static readonly Vector2 HutCenterOffset = new(0f, 1.5f);
        private static readonly Vector2 HutDimensions = new(3f, 3f);
        private readonly OccupancyGrid grid;
        internal List<Hut> Huts { get; } = new();

        internal HouseholdSystem(OccupancyGrid grid) => this.grid = grid;

        internal void Reset()
        {
            foreach (Hut hut in Huts)
                grid.Release(hut.Footprint, CellOccupancy.Hut);
            Huts.Clear();
            TryBuild(Sites[0]);
            TryBuild(Sites[1]);
        }

        private Hut TryBuild(Vector2 position)
        {
            RectInt footprint = grid.FootprintAt(position + HutCenterOffset, HutDimensions);
            if (!grid.TryOccupy(footprint, CellOccupancy.Hut))
                return null;

            var hut = new Hut(position, footprint);
            Huts.Add(hut);
            return hut;
        }

        private Hut FindAndBuild()
        {
            foreach (Vector2 site in Sites)
            {
                Hut hut = TryBuild(site);
                if (hut != null)
                    return hut;
            }

            // Nearby clear plots are considered if vegetation occupies the preferred sites.
            for (int radius = 1; radius <= 5; radius++)
                for (int z = -radius; z <= radius; z++)
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != radius)
                            continue;
                        Hut hut = TryBuild(new Vector2(x * 4f, z * 4f));
                        if (hut != null)
                            return hut;
                    }
            return null;
        }

        internal void MoveIn(CharacterState person, Hut hut)
        {
            person.Hut?.Residents.Remove(person);
            person.Hut = hut;
            hut?.Residents.Add(person);
        }

        internal bool MoveTogether(CharacterState first, CharacterState second, out CharacterState displaced)
        {
            displaced = null;
            if (first.Hut != null && first.Hut == second.Hut)
                return true;
            CharacterState groom = first.Gender == ResidentGender.Male ? first : second;
            CharacterState bride = groom == first ? second : first;
            Hut sharedHome = groom.Hut;
            if (sharedHome != null)
            {
                foreach (CharacterState roommate in sharedHome.Residents)
                {
                    if (roommate == groom || !roommate.IsAdult || roommate.Gender != ResidentGender.Male
                        || roommate.Spouse != null)
                        continue;

                    // Moving the bride first makes her former bed available to the roommate.
                    MoveFamily(bride, sharedHome);
                    MoveFamily(roommate, FindVacancy(1, sharedHome));
                    displaced = roommate;
                    return true;
                }
            }

            if (HasSpace(first.Hut, 1))
                MoveFamily(second, first.Hut);
            else if (HasSpace(second.Hut, 1))
                MoveFamily(first, second.Hut);
            else
            {
                Hut home = FindVacancy(2);
                if (home == null)
                    return false;
                MoveFamily(first, home);
                MoveFamily(second, home);
            }
            return true;
        }

        private void MoveFamily(CharacterState parent, Hut home)
        {
            Hut previousHome = parent.Hut;
            MoveIn(parent, home);
            if (previousHome == null)
                return;

            for (int i = previousHome.Residents.Count - 1; i >= 0; i--)
            {
                CharacterState child = previousHome.Residents[i];
                if (child.IsAdult || (child.FirstParent != parent.Id && child.SecondParent != parent.Id))
                    continue;

                int otherParent = child.FirstParent == parent.Id ? child.SecondParent : child.FirstParent;
                if (!previousHome.Residents.Exists(resident => resident.Id == otherParent))
                    MoveIn(child, home);
            }
        }

        internal Hut FindVacancy(int beds, Hut excluded = null)
        {
            Hut best = null;
            int fewestAdults = AdultCapacity + 1;
            foreach (Hut hut in Huts)
            {
                if (hut == excluded)
                    continue;
                int adults = hut.AdultCount;
                if (adults + beds > AdultCapacity || adults >= fewestAdults)
                    continue;
                best = hut;
                fewestAdults = adults;
                if (adults == 0)
                    break;
            }
            return best;
        }

        internal void HouseHomelessAdults(TrialWorld world)
        {
            CharacterState firstWithoutBed = null;
            foreach (CharacterState person in world.People)
            {
                if (!person.IsAdult || person.Hut != null)
                    continue;

                Hut occupiedChildhoodHome = person.ChildhoodHut?.AdultCount > 0 ? person.ChildhoodHut : null;
                Hut vacancy = FindVacancy(1, occupiedChildhoodHome);
                if (vacancy != null)
                {
                    MoveFamily(person, vacancy);
                    world.RecordEvent($"{person.Name} moved into an existing hut");
                    continue;
                }

                if (firstWithoutBed == null)
                    firstWithoutBed = person;
                else if (Huts.Count < Sites.Length)
                {
                    Hut hut = FindAndBuild();
                    if (hut == null)
                        return;
                    MoveFamily(firstWithoutBed, hut);
                    MoveFamily(person, hut);
                    world.RecordEvent($"{firstWithoutBed.Name} and {person.Name} built a hut");
                    return;
                }
            }
        }

        internal static bool HasSpace(Hut hut, int count) =>
            hut != null && hut.AdultCount + count <= AdultCapacity;
    }
}
