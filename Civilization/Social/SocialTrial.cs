using System.Collections.Generic;
using UnityEngine;

namespace Civilization
{
    internal sealed class SocialTrial
    {
        internal const float TalkRangeSquared = 1.25f * 1.25f;
        private const float MeetingLength = 4f;
        private const float MatingInterval = 0.15f * CharacterState.DayLength;
        private const float TrustThreshold = 0.55f;
        private const float DriveThreshold = 0.65f;
        private const float TrustPerSecond = 0.09f;
        private const float SingleDrivePerSecond = 0.012f;
        private static readonly Vector2 CourtshipMeetingPoint = new(0f, -2f);
        private const float MarriedDrivePerSecond = 0.024f;
        internal float MarriedMatingMultiplier { get; set; } = 1f;

        private sealed class Relationship
        {
            internal float Trust;
            internal float ContactTime;
            internal int LastContact;
            internal float MeetAgainAt;
            internal int Matings;
        }

        private readonly Dictionary<(int First, int Second), Relationship> relationships = new();
        private readonly List<(CharacterState First, CharacterState Second, Relationship Bond)> meetings = new();
        private readonly List<CharacterState> availableSingles = new();

        internal int Matings { get; private set; }

        private static (int, int) Key(CharacterState first, CharacterState second) =>
            first.Id < second.Id ? (first.Id, second.Id) : (second.Id, first.Id);

        internal float TrustFor(CharacterState person)
        {
            var partner = person.Spouse ?? person.SocialPartner ?? person.LastSocialPartner;
            return partner != null && relationships.TryGetValue(Key(person, partner), out Relationship bond)
                ? bond.Trust : 0f;
        }

        internal void Plan(TrialWorld world)
        {
            meetings.Clear();
            availableSingles.Clear();
            foreach (CharacterState person in world.People)
            {
                person.SocialPartner = null;
                person.SocialTarget = null;
                if (person.IsAdult && person.Spouse == null && person.CanSocialize)
                    availableSingles.Add(person);
            }

            foreach (CharacterState first in world.People)
            {
                CharacterState spouse = first.Spouse;
                if (spouse == null || first.Id > spouse.Id || !first.CanSocialize || !spouse.CanSocialize
                    || MarriedMatingMultiplier <= 0f || !Willing(first, spouse)
                    || world.Population.MatingStatus(first, spouse) != MatingAvailability.Available)
                    continue;

                Vector2? meetingPlace = (first.Position - spouse.Position).sqrMagnitude >
                    CharacterState.PerceptionRange * CharacterState.PerceptionRange
                    ? first.Hut.Position - Vector2.up : null;
                ScheduleMeeting(first, spouse, meetingPlace);
            }

            foreach (CharacterState first in availableSingles)
            {
                if (first.SocialPartner != null)
                    continue;

                CharacterState partner = null;
                float bestTrust = -1f;
                float bestDistance = float.PositiveInfinity;
                foreach (CharacterState second in availableSingles)
                {
                    if (second == first || second.SocialPartner != null || !CanPair(first, second))
                        continue;

                    float distance = (first.Position - second.Position).sqrMagnitude;
                    relationships.TryGetValue(Key(first, second), out Relationship bond);
                    float trust = bond?.Trust ?? 0f;
                    if (trust < bestTrust || (trust == bestTrust && distance >= bestDistance))
                        continue;

                    partner = second;
                    bestTrust = trust;
                    bestDistance = distance;
                }

                if (partner != null)
                {
                    float range = CharacterState.PerceptionRange * CharacterState.PerceptionRange;
                    ScheduleMeeting(first, partner, bestDistance > range ? CourtshipMeetingPoint : null);
                }
            }

            // Unpaired adults can still socialize and exchange resource discoveries nearby.
            foreach (CharacterState first in availableSingles)
            {
                if (first.SocialPartner != null)
                    continue;

                foreach (CharacterState second in availableSingles)
                {
                    if (second == first || second.SocialPartner != null
                        || (first.Loneliness < 0.5f && second.Loneliness < 0.5f)
                        || (first.Position - second.Position).sqrMagnitude > CharacterState.PerceptionRange * CharacterState.PerceptionRange)
                        continue;

                    if (relationships.TryGetValue(Key(first, second), out Relationship bond)
                        && world.TimeAlive < bond.MeetAgainAt)
                        continue;

                    ScheduleMeeting(first, second);
                    break;
                }
            }
        }

        private void ScheduleMeeting(CharacterState first, CharacterState second, Vector2? meetingPlace = null)
        {
            var key = Key(first, second);
            if (!relationships.TryGetValue(key, out Relationship relationship))
                relationships.Add(key, relationship = new Relationship());
            first.SocialPartner = second;
            second.SocialPartner = first;
            first.SocialTarget = meetingPlace ?? second.Position;
            second.SocialTarget = meetingPlace ?? first.Position;
            meetings.Add((first, second, relationship));
        }

        internal void Resolve(TrialWorld world)
        {
            foreach (CharacterState person in world.People)
            {
                bool talking = person.SocialPartner != null && person.Action == "Socializing"
                    && person.SocialPartner.Action == "Socializing";
                person.Loneliness = Mathf.Clamp01(person.Loneliness + CharacterState.Step * (talking ? -0.12f : 0.012f));
                if (person.IsAdult)
                {
                    float rate = person.Spouse == null ? SingleDrivePerSecond
                        : MarriedDrivePerSecond * Mathf.Max(0f, MarriedMatingMultiplier);
                    person.MatingDrive = Mathf.Clamp01(person.MatingDrive + CharacterState.Step * rate);
                }
            }

            foreach (var (first, second, bond) in meetings)
            {
                if (first.SocialPartner != second || first.Action != "Socializing" || second.Action != "Socializing"
                    || (first.Position - second.Position).sqrMagnitude > TalkRangeSquared)
                    continue;

                if (bond.LastContact != world.TickNumber - 1)
                    bond.ContactTime = 0f;
                if (bond.ContactTime == 0f)
                {
                    int reports = first.ShareDiscoveriesWith(second) + second.ShareDiscoveriesWith(first);
                    if (reports > 0)
                        world.RecordEvent($"Shared {reports} resource discoveries");
                }

                first.LastSocialPartner = second;
                second.LastSocialPartner = first;
                bond.Trust = Mathf.Clamp01(bond.Trust + CharacterState.Step * TrustPerSecond);
                bond.LastContact = world.TickNumber;
                bond.ContactTime += CharacterState.Step;
                if (bond.ContactTime < MeetingLength)
                    continue;

                if (bond.Trust >= TrustThreshold && Willing(first, second))
                    TryStartFamily(world, first, second, bond);

                bond.ContactTime = 0f;
                if (!CanPair(first, second))
                    bond.MeetAgainAt = world.TimeAlive + 12f;
            }
        }

        internal void TeachChildren(TrialWorld world)
        {
            foreach (CharacterState child in world.People)
            {
                if (child.IsAdult)
                    continue;
                foreach (CharacterState parent in world.People)
                    if ((parent.Id == child.FirstParent || parent.Id == child.SecondParent)
                        && (child.Position - parent.Position).sqrMagnitude <= TalkRangeSquared)
                        parent.ShareDiscoveriesWith(child);
            }
        }

        private static bool Willing(CharacterState first, CharacterState second) =>
            first.MatingDrive >= DriveThreshold && second.MatingDrive >= DriveThreshold && CanPair(first, second);

        private static bool CanPair(CharacterState first, CharacterState second)
        {
            return first.IsAdult && second.IsAdult && first.Gender != second.Gender
                && (first.Spouse == null || first.Spouse == second)
                && (second.Spouse == null || second.Spouse == first)
                && first.FirstParent != second.Id && first.SecondParent != second.Id
                && second.FirstParent != first.Id && second.SecondParent != first.Id
                && (first.FirstParent < 0 || (first.FirstParent != second.FirstParent && first.FirstParent != second.SecondParent))
                && (first.SecondParent < 0 || (first.SecondParent != second.FirstParent && first.SecondParent != second.SecondParent));
        }

        private void TryStartFamily(TrialWorld world, CharacterState first, CharacterState second, Relationship bond)
        {
            if (first.Spouse == null)
            {
                if (!world.Housing.MoveTogether(first, second, out CharacterState roommate))
                    return;
                if (roommate != null)
                    world.RecordEvent(roommate.Hut == null ? $"{roommate.Name} moved out and needs a bed"
                        : $"{roommate.Name} moved to another hut to make room for the couple");
                first.Spouse = second;
                second.Spouse = first;
                world.RecordEvent($"{first.Name} and {second.Name} married and moved in together");
            }

            if (MarriedMatingMultiplier <= 0f
                || world.Population.MatingStatus(first, second) != MatingAvailability.Available)
                return;

            Matings++;
            bond.Matings++;
            world.RecordEvent($"{first.Name} and {second.Name} mated");
            first.MatingDrive = second.MatingDrive = 0.1f;
            CharacterState mother = first.Gender == ResidentGender.Female ? first : second;
            CharacterState partner = mother == first ? second : first;
            world.Population.TryConceive(mother, partner);
            first.EarliestMatingTime = second.EarliestMatingTime = world.TimeAlive + MatingInterval;
        }

        internal int MatingsFor(CharacterState person) =>
            person.Spouse != null && relationships.TryGetValue(Key(person, person.Spouse), out Relationship bond)
                ? bond.Matings : 0;

        internal void Forget(CharacterState person, List<CharacterState> people)
        {
            if (person.SocialPartner != null)
            {
                person.SocialPartner.SocialPartner = null;
                person.SocialPartner.SocialTarget = null;
            }
            person.SocialPartner = null;
            foreach (CharacterState other in people)
                if (other.LastSocialPartner == person)
                    other.LastSocialPartner = null;
            var expired = new List<(int, int)>();
            foreach (var key in relationships.Keys)
                if (key.First == person.Id || key.Second == person.Id)
                    expired.Add(key);
            foreach (var key in expired)
                relationships.Remove(key);
        }

        internal void Reset()
        {
            relationships.Clear();
            meetings.Clear();
            availableSingles.Clear();
            Matings = 0;
        }
    }
}
