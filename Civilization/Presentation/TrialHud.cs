using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class TrialHud : System.IDisposable
    {
        private readonly PanelSettings panelSettings;
        private readonly VisualElement toolbar;
        private readonly VisualElement bottomHud;
        private readonly BuildingPalette buildingPalette;
        private readonly VisualElement debug;
        private readonly DebugTabs tabs;
        private readonly Label clock;
        private readonly Label settlement;
        private readonly Label resident;
        private readonly Label household;
        private readonly Label activity;
        private readonly Label resources;
        private readonly Label characterDetails;
        private readonly DebugLogView log;
        private readonly DebugParameters parameters;
        private readonly Label performance;
        private readonly Label pineCensus;
        private readonly HutInspector hutInspector;
        private readonly TimelapseControl timelapse = new();
        private readonly ProgressBar hunger, thirst, motivation, stress, loneliness, trust, drive;
        private readonly TrialWorld world;
        private int selectedId;
        private float refresh;

        internal int Speed => timelapse.Speed;
        private bool IsDebugVisible => debug.style.display.value != DisplayStyle.None;
        internal bool IsCharacterDebugVisible => IsDebugVisible && tabs.SelectedIndex == 0;
        internal bool IsEditingText => toolbar.panel?.focusController.focusedElement is VisualElement focused
            && (focused.ClassListContains("unity-text-input") || focused.GetFirstAncestorOfType<TextField>() != null
                || focused.GetFirstAncestorOfType<FloatField>() != null || focused.GetFirstAncestorOfType<IntegerField>() != null);

        internal TrialHud(GameObject owner, TrialWorld world, System.Action<float> setMatingMultiplier,
            System.Action<bool> setGridVisible, BuildingPalette buildingPalette)
        {
            this.world = world;
            this.buildingPalette = buildingPalette;
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("TrialTheme");
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            VisualElement root = document.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("SettlementHud"));

            bottomHud = new VisualElement();
            bottomHud.AddToClassList("trial-bottom-hud");
            root.Add(bottomHud);
            bottomHud.Add(buildingPalette);
            toolbar = new VisualElement();
            toolbar.AddToClassList("trial-toolbar");
            bottomHud.Add(toolbar);

            toolbar.Add(timelapse);
            var toggle = new Button { text = "Open debug" };
            toggle.clicked += () => ToggleDebug(toggle);
            toolbar.Add(toggle);
            clock = AddLabel(toolbar, "", "trial-clock");
            settlement = AddLabel(toolbar, "", "trial-subtle");

            debug = new VisualElement();
            debug.AddToClassList("trial-panel");
            root.Add(debug);
            AddLabel(debug, "Settlement debug", "trial-title");

            tabs = new DebugTabs();
            tabs.SelectionChanged += () => refresh = 0;
            debug.Add(tabs);
            VisualElement character = tabs.AddTab("Character");
            VisualElement huts = tabs.AddTab("Huts");
            VisualElement tuning = tabs.AddTab("Parameters");
            VisualElement logs = tabs.AddTab("Log");

            hutInspector = new HutInspector(world);
            huts.Add(hutInspector);
            parameters = new DebugParameters(world, setMatingMultiplier);
            tuning.Add(parameters);
            VisualElement generatedWorld = AddCard(tuning, "Generated world");
            AddLabel(generatedWorld,
                $"{world.Grid.Size} × {world.Grid.Size} units  ·  Seed {world.Ecology.ActiveSeed}", "trial-copy");
            var gridToggle = new Toggle("Show grid");
            gridToggle.RegisterValueChangedCallback(change => setGridVisible(change.newValue));
            generatedWorld.Add(gridToggle);
            pineCensus = AddLabel(AddCard(tuning, "Tree lifecycle"), "", "trial-copy");
            log = new DebugLogView();
            AddCard(logs, "Settlement events · newest first").Add(log);
            performance = AddLabel(AddCard(logs, "Performance"), "", "trial-subtle");

            var selector = new VisualElement();
            selector.AddToClassList("trial-selector");
            character.Add(selector);
            selector.Add(new Button(() => SelectRelative(-1)) { text = "‹", tooltip = "Previous resident" });
            resident = AddLabel(selector, "", "trial-resident");
            selector.Add(new Button(() => SelectRelative(1)) { text = "›", tooltip = "Next resident" });
            household = AddLabel(character, "", "trial-subtle");

            VisualElement vitals = AddCard(character, "Vitals");
            hunger = AddBar(vitals, "Hunger", "meter-hunger");
            thirst = AddBar(vitals, "Thirst", "meter-thirst");
            motivation = AddBar(vitals, "Motivation", "meter-motivation");
            stress = AddBar(vitals, "Stress", "meter-stress");

            VisualElement social = AddCard(character, "Social");
            loneliness = AddBar(social, "Loneliness", "meter-social");
            trust = AddBar(social, "Trust", "meter-social");
            drive = AddBar(social, "Mating drive", "meter-social");

            VisualElement decisions = AddCard(character, "Activity");
            activity = AddLabel(decisions, "", "trial-copy");
            resources = AddLabel(decisions, "", "trial-subtle");
            characterDetails = AddLabel(AddCard(character, "Resident details"), "", "trial-copy");
            debug.style.display = DisplayStyle.None;
        }

        internal bool BlocksCamera(Vector2 screenPosition)
        {
            if (toolbar.panel == null)
                return false;

            screenPosition.y = Screen.height - screenPosition.y;
            Vector2 point = RuntimePanelUtils.ScreenToPanel(toolbar.panel, screenPosition);
            return bottomHud.worldBound.Contains(point) || (IsDebugVisible && debug.worldBound.Contains(point));
        }

        internal void RefreshBuildingPalette() => buildingPalette.Refresh();

        private void ToggleDebug(Button button)
        {
            debug.style.display = IsDebugVisible ? DisplayStyle.None : DisplayStyle.Flex;
            button.text = IsDebugVisible ? "Close debug" : "Open debug";
            refresh = 0;
        }

        private static VisualElement AddCard(VisualElement parent, string title)
        {
            var card = new VisualElement();
            card.AddToClassList("trial-card");
            AddLabel(card, title, "trial-section");
            parent.Add(card);
            return card;
        }

        private static Label AddLabel(VisualElement parent, string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static ProgressBar AddBar(VisualElement parent, string title, string className)
        {
            var bar = new ProgressBar { title = title, highValue = 1 };
            bar.AddToClassList("trial-meter");
            bar.AddToClassList(className);
            parent.Add(bar);
            return bar;
        }

        internal CharacterState SelectedPerson(TrialWorld trial)
        {
            foreach (CharacterState person in trial.People)
                if (person.Id == selectedId)
                    return person;
            return trial.People.Count > 0 ? trial.People[0] : null;
        }

        private void SelectRelative(int direction)
        {
            if (world == null || world.People.Count == 0)
                return;
            int index = world.People.IndexOf(SelectedPerson(world));
            int next = (index + direction + world.People.Count) % world.People.Count;
            selectedId = world.People[next].Id;
            refresh = 0;
        }

        internal void Refresh(TrialWorld trial, float delta, double tickMs)
        {
            refresh -= delta;
            if (refresh > 0)
                return;
            refresh = 0.25f;

            int minutes = (int)(trial.Hour * 60);
            clock.text = $"{trial.Ecology.SeasonName.ToUpperInvariant()} / {trial.Ecology.WeatherName.ToUpperInvariant()}  ·  DAY {trial.Day + 1}   {minutes / 60:00}:{minutes % 60:00}";
            settlement.text = $"{trial.People.Count} residents  ·  {trial.Housing.Huts.Count} huts  ·  {trial.Ecology.BushCount}/{trial.Ecology.BushLimit} bushes";
            if (!IsDebugVisible)
                return;

            switch (tabs.SelectedIndex)
            {
                case 1:
                    hutInspector.Refresh();
                    return;
                case 2:
                    parameters.Refresh();
                    var ecology = trial.Ecology;
                    pineCensus.text = $"{ecology.PineSaplings} saplings  ·  {ecology.PineYoung} young\n"
                        + $"{ecology.PineMature} mature ({ecology.ConeBearingPines} seeding)  ·  {ecology.PineOld} old\n"
                        + $"{ecology.LeaflessPines} leafless  ·  {ecology.PineSeeds} dormant seeds  ·  {ecology.PinesLost} died";
                    return;
                case 3:
                    log.Refresh(trial);
                    performance.text = $"Tick {tickMs:F3} ms  ·  Unity {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F1} MiB"
                        + $"\nManaged heap {Profiler.GetMonoUsedSizeLong() / 1048576f:F1} MiB";
                    return;
            }

            CharacterState person = SelectedPerson(trial);
            if (person == null)
                return;
            selectedId = person.Id;
            string ageGroup = person.IsAdult ? "Adult" : "Child";
            resident.text = $"{HutInspector.Badge(person)} {person.Name}  ·  {ageGroup}, {(int)person.Age}";
            string home = person.Hut == null ? "Homeless"
                : $"Hut {trial.Housing.Huts.IndexOf(person.Hut) + 1} · {person.Hut.Residents.Count} residents";
            household.text = person.Spouse == null ? home : $"{home} · Married to {person.Spouse.Name}";
            hunger.value = person.Hunger;
            thirst.value = person.Thirst;
            motivation.value = person.Motivation;
            stress.value = person.Stress;
            loneliness.value = person.Loneliness;
            trust.value = trial.Social.TrustFor(person);
            drive.value = person.IsAdult ? person.MatingDrive : 0f;
            drive.style.display = person.IsAdult ? DisplayStyle.Flex : DisplayStyle.None;

            string emergency = person.FightOrFlight ? " · Fight-or-flight" : "";
            activity.text = $"{person.Action} · {person.ActionAge:F1}s{emergency}\n{person.Reason}";
            resources.text = $"{person.DiscoveredCount} known resources · {person.ReportsReceived} shared · {CharacterState.PerceptionRange:0.#} m perception"
                + (person.HasPreservedFruit ? "\nCarrying one saved fruit portion" : "");
            var memory = person.Memory;
            string relationship = person.Spouse != null
                ? $"Married to {person.Spouse.Name} · {trial.Social.MatingsFor(person)} matings"
                : person.IsAdult ? "Single" : "Child";
            string pregnancy = trial.Population.IsExpecting(person) ? " · Expecting a child" : "";
            characterDetails.text = $"{person.Gender} · Age {person.Age:F1} / {trial.Population.Lifespan:F1} years\n"
                + $"{relationship}{pregnancy}\nPosition {person.Position.x:F1}, {person.Position.y:F1}\n"
                + $"Time left without food {person.FoodTimeLeft:F2} days · Water {person.WaterTimeLeft:F2} days\n"
                + $"Well {(person.KnowsWell ? "known" : "undiscovered")} · Decision {person.DecisionMs:F3} ms\n"
                + $"Death memory: {memory.HungerDeaths} hunger, {memory.ThirstDeaths} thirst\n"
                + (memory.Cause == null ? "Last death: none" : $"Last death: {memory.Cause} while {memory.Action}");
        }

        public void Dispose() => Object.Destroy(panelSettings);
    }
}

