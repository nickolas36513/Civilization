using System.Linq;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class DebugLogView : VisualElement
    {
        private readonly Label totals = new();
        private readonly Label history = new();
        private int version = -1;

        internal DebugLogView()
        {
            totals.AddToClassList("trial-section");
            totals.AddToClassList("trial-copy");
            history.AddToClassList("trial-copy");
            history.AddToClassList("trial-log-history");
            Add(totals);
            Add(history);
        }

        internal void Refresh(TrialWorld world)
        {
            var population = world.Population;
            totals.text = $"{population.Births} births · {population.PendingBirths} expected · {world.Social.Matings} matings\n"
                + $"{population.Arrivals} arrivals · {population.Deaths} deaths\n"
                + $"{population.HungerDeaths} hunger deaths · {population.ThirstDeaths} thirst deaths"
                + $"\n{world.Ecology.AvailableFruit} ripe portions · {population.SavedFruit} carried · {population.HungryResidents} hungry"
                + $"\nGrid {world.Grid.OccupiedCount}/{world.Grid.CellCount} cells occupied"
                + (population.FoodScarce ? "\nFood scarce · new pregnancies and arrivals paused" : "");

            if (version == world.EventVersion)
                return;

            version = world.EventVersion;
            history.text = string.Join("\n", world.RecentEvents.Reverse());
        }
    }
}
