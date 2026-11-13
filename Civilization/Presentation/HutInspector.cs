using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class HutInspector : VisualElement
    {
        private readonly TrialWorld world;
        private readonly List<(VisualElement Root, Label Roster)> cards = new();
        private readonly StringBuilder roster = new();

        internal HutInspector(TrialWorld world) => this.world = world;

        internal static string Badge(CharacterState person) =>
            person.Gender == ResidentGender.Male ? "[M]" : "[F]";

        internal void Refresh()
        {
            var huts = world.Housing.Huts;
            while (cards.Count > huts.Count)
            {
                int last = cards.Count - 1;
                cards[last].Root.RemoveFromHierarchy();
                cards.RemoveAt(last);
            }
            while (cards.Count < huts.Count)
                AddHutCard(cards.Count + 1);

            for (int i = 0; i < huts.Count; i++)
            {
                HouseholdSystem.Hut hut = huts[i];
                roster.Clear();
                roster.Append($"{hut.AdultCount}/{HouseholdSystem.AdultCapacity} adults · {hut.ChildCount} children");
                if (hut.Residents.Count == 0)
                    roster.Append("\nVacant");

                foreach (CharacterState person in hut.Residents)
                {
                    roster.AppendLine();
                    roster.Append($"{Badge(person)} {person.Name} · {(int)person.Age} · ");
                    AppendStatus(person);
                }

                cards[i].Roster.text = roster.ToString();
            }
        }

        private void AddHutCard(int number)
        {
            var card = new VisualElement();
            card.AddToClassList("trial-card");
            var title = new Label($"Hut {number}");
            title.AddToClassList("trial-section");
            var details = new Label();
            details.AddToClassList("trial-copy");
            card.Add(title);
            card.Add(details);
            Add(card);
            cards.Add((card, details));
        }

        private void AppendStatus(CharacterState person)
        {
            if (!person.IsAdult)
            {
                roster.Append("Child");
                return;
            }
            if (person.Spouse == null)
            {
                roster.Append(world.Population.IsExpecting(person) ? "Expecting" : "Seeking a mate");
                return;
            }

            roster.Append($"Married to {person.Spouse.Name} · {world.Social.MatingsFor(person)} matings");
            string status = world.Population.MatingStatus(person, person.Spouse) switch
            {
                MatingAvailability.NoSharedHut => "Waiting for a shared hut",
                MatingAvailability.ChildLimit => "Mating paused · two children",
                MatingAvailability.Expecting => "Expecting",
                MatingAvailability.Recovery => "Recovering",
                _ when world.Social.MarriedMatingMultiplier <= 0f => "Mating disabled",
                _ when person.Reason == "Spending time with spouse" => "Together",
                _ => ""
            };
            if (status.Length > 0)
                roster.Append($" · {status}");
        }
    }
}
