using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class DebugParameters : VisualElement
    {
        private readonly TrialWorld world;
        private readonly FloatField mating;

        internal DebugParameters(TrialWorld world, Action<float> setMating)
        {
            this.world = world;
            var survival = AddGroup("Survival");
            AddFloat(survival, "Days without food", world.DaysWithoutFood, 0.1f, 365f, value => world.DaysWithoutFood = value);
            AddFloat(survival, "Days without water", world.DaysWithoutWater, 0.1f, 365f, value => world.DaysWithoutWater = value);
            AddFloat(survival, "Age gained per day (years)", world.Population.YearsPerDay, 0f, 50f, value => world.Population.YearsPerDay = value);
            AddFloat(survival, "Lifespan (years)", world.Population.Lifespan, PopulationSystem.AdultAge + 1f, 500f, value => world.Population.Lifespan = value);

            var population = AddGroup("Population");
            mating = AddFloat(population, "Married mating drive (×)", world.Social.MarriedMatingMultiplier, 0f, float.MaxValue, setMating);
            AddFloat(population, "Pregnancy duration (days)", world.Population.PregnancyDays, 0.05f, 365f, value => world.Population.PregnancyDays = value);
            AddFloat(population, "Days between arrivals", world.Population.ArrivalIntervalDays, 0.1f, 365f, value => world.Population.ArrivalIntervalDays = value);
            AddInt(population, "Newcomers below (0 = off)", world.Population.NewcomerTarget, value => world.Population.NewcomerTarget = value);
            AddInt(population, "Population limit (0 = unlimited)", world.Population.PopulationLimit, value => world.Population.PopulationLimit = value);

            var ecology = AddGroup("Ecosystem");
            AddFloat(ecology, "Tree lifecycle speed (×)", world.Ecology.PineLifecycleSpeed, 0f, 8f, value => world.Ecology.PineLifecycleSpeed = value);
        }

        internal void Refresh()
        {
            if (mating.value != world.Social.MarriedMatingMultiplier)
                mating.SetValueWithoutNotify(world.Social.MarriedMatingMultiplier);
        }

        private VisualElement AddGroup(string title)
        {
            var group = new VisualElement();
            group.AddToClassList("trial-card");
            var heading = new Label(title);
            heading.AddToClassList("trial-section");
            group.Add(heading);
            Add(group);
            return group;
        }

        private static FloatField AddFloat(VisualElement parent, string title, float initial, float min, float max, Action<float> apply)
        {
            var field = new FloatField { value = initial, isDelayed = true, tooltip = title };
            field.RegisterValueChangedCallback(change =>
            {
                float input = change.newValue;
                float value = float.IsNaN(input) || float.IsInfinity(input)
                    ? change.previousValue : Mathf.Clamp(input, min, max);
                field.SetValueWithoutNotify(value);
                apply(value);
            });
            AddRow(parent, title, field);
            return field;
        }

        private static void AddInt(VisualElement parent, string title, int initial, Action<int> apply)
        {
            var field = new IntegerField { value = initial, isDelayed = true, tooltip = title };
            field.RegisterValueChangedCallback(change =>
            {
                int value = Mathf.Max(0, change.newValue);
                field.SetValueWithoutNotify(value);
                apply(value);
            });
            AddRow(parent, title, field);
        }

        private static void AddRow(VisualElement parent, string title, VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("trial-parameter");
            var label = new Label(title);
            label.AddToClassList("trial-parameter-label");
            field.AddToClassList("trial-number");
            row.Add(label);
            row.Add(field);
            parent.Add(row);
        }
    }
}
