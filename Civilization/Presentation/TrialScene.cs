using Civilization.Ecology;
using System.Collections.Generic;
using UnityEngine;

namespace Civilization
{
    public sealed class TrialScene : MonoBehaviour
    {

        private const float TurnDegreesPerSecond = 720f;
        private const int MaxStepsPerFrame = 64;

        [SerializeField] private GameObject bodyPrefab;
        [Tooltip("Yaw correction if the body mesh faces away from its local +Z axis.")]
        [SerializeField] private float bodyYawOffset;
        [SerializeField] private GameObject hutPrefab;
        [SerializeField] private GameObject wellPrefab;
        [SerializeField] private Ecosystem ecology;
        [Tooltip("Decorative grass patches generated from the Nature Library.")]
        [SerializeField, Range(0, 256)] private int grassPatches = 96;
        [SerializeField] private BuildingDefinition[] buildings = System.Array.Empty<BuildingDefinition>();
        [Tooltip("How quickly married residents regain mating drive. Zero pauses mating for married couples.")]
        [SerializeField, Min(0f)] private float marriedMatingMultiplier = 2f;

        private readonly System.Diagnostics.Stopwatch tickTimer = new();
        private TrialWorld trial;
        private Light sun;
        private Camera overview;
        private MapCameraControls cameraControls;
        private BuildingPlacement buildingPlacement;
        private readonly Dictionary<CharacterState, Transform> characters = new();
        private readonly List<CharacterState> departed = new();
        private readonly List<GameObject> huts = new();
        private int displayedGeneration;
        private Renderer terrain;
        private MaterialPropertyBlock terrainColor;
        private int displayedDay = -1;
        private TrialHud hud;
        private Material material;
        private MeshRenderer gridOverlay;
        private Mesh gridOverlayMesh;
        private Material gridOverlayMaterial;
        private LineRenderer perceptionRing;
        private float backlog;
        private double tickMs;

        private void Awake()
        {
            terrainColor = new MaterialPropertyBlock();
            trial = new TrialWorld(ecology);
            ecology.Initialize(trial.Grid, grassPatches);
            trial.Social.MarriedMatingMultiplier = marriedMatingMultiplier;
            overview = Camera.main;
            foreach (Light candidate in FindObjectsByType<Light>())
            {
                if (candidate.type != LightType.Directional)
                    continue;
                sun = candidate;
                break;
            }

            if (sun == null)
            {
                var lightObject = new GameObject("Trial Sun");
                lightObject.transform.SetParent(transform);
                sun = lightObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.EnableKeyword("_EMISSION");
            int mapSize = trial.Grid.Size;
            terrain = Shape("Biome Ground", PrimitiveType.Cube, Vector2.zero, -0.25f,
                new Vector3(mapSize, 0.5f, mapSize), ecology.GroundColor).GetComponent<Renderer>();
            PlaceModel(wellPrefab, "Water Well", TrialWorld.WellPosition, 1.2f);

            BuildPerceptionRing();
            buildingPlacement = new BuildingPlacement(trial.Grid, overview, transform);
            hud = new TrialHud(gameObject, trial, value => marriedMatingMultiplier = value, SetGridVisible,
                new BuildingPalette(buildings, buildingPlacement));
            if (overview != null)
                cameraControls = new MapCameraControls(overview, hud, mapSize);
        }

        private void SetGridVisible(bool visible)
        {
            if (visible && gridOverlay == null)
            {
                Shader shader = Resources.Load<Shader>("DebugGrid");
                if (shader == null || !shader.isSupported)
                {
                    Debug.LogError("Cannot show the grid: the DebugGrid shader is missing or unsupported.", this);
                    return;
                }

                int size = trial.Grid.Size;
                float half = size * 0.5f;
                var overlay = new GameObject("Occupancy Grid Overlay");
                overlay.transform.SetParent(transform, false);
                overlay.transform.position = new Vector3(0f, 0.02f, 0f);
                gridOverlayMesh = new Mesh { name = "Debug Grid Surface" };
                gridOverlayMesh.vertices = new Vector3[] { new(-half, 0, -half), new(-half, 0, half), new(half, 0, half), new(half, 0, -half) };
                gridOverlayMesh.uv = new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
                gridOverlayMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                overlay.AddComponent<MeshFilter>().sharedMesh = gridOverlayMesh;
                gridOverlayMaterial = new Material(shader);
                gridOverlayMaterial.SetFloat("_GridSize", size);
                gridOverlay = overlay.AddComponent<MeshRenderer>();
                gridOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gridOverlay.sharedMaterial = gridOverlayMaterial;
            }

            if (gridOverlay != null)
                gridOverlay.enabled = visible;
        }

        private GameObject PlaceModel(GameObject prefab, string label, Vector2 point, float height)
        {
            GameObject model = Instantiate(prefab, transform);
            model.name = label;
            model.transform.position = new Vector3(point.x, 0f, point.y);

            // Use the visible mesh bounds because the imported models have different pivots and scales.
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float scale = height / bounds.size.y;
            model.transform.localScale *= scale;
            Vector3 baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position -= (baseCenter - model.transform.position) * scale;
            return model;
        }

        private GameObject Shape(string label, PrimitiveType type, Vector2 position, float height, Vector3 scale, Color color)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = label;
            shape.transform.SetParent(transform);
            shape.transform.position = new Vector3(position.x, height, position.y);
            shape.transform.localScale = scale;
            var renderer = shape.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
            Destroy(shape.GetComponent<Collider>());
            return shape;
        }


        private void BuildPerceptionRing()
        {
            const int segments = 64;
            var ringObject = new GameObject("Perception Range");
            ringObject.transform.SetParent(transform);
            perceptionRing = ringObject.AddComponent<LineRenderer>();
            perceptionRing.useWorldSpace = false;
            perceptionRing.loop = true;
            perceptionRing.alignment = LineAlignment.TransformZ;
            perceptionRing.transform.rotation = Quaternion.Euler(90, 0, 0);
            perceptionRing.widthMultiplier = 0.12f;
            perceptionRing.positionCount = segments;
            perceptionRing.sharedMaterial = material;
            perceptionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            perceptionRing.receiveShadows = false;

            var properties = new MaterialPropertyBlock();
            Color cyan = new Color(0.18f, 0.95f, 1f);
            properties.SetColor("_BaseColor", cyan);
            properties.SetColor("_EmissionColor", cyan);
            perceptionRing.SetPropertyBlock(properties);

            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                perceptionRing.SetPosition(i, CharacterState.PerceptionRange *
                    new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0));
            }
            perceptionRing.enabled = false;
        }
        private void Update()
        {
            trial.Social.MarriedMatingMultiplier = marriedMatingMultiplier;
            float delta = Time.unscaledDeltaTime;
            backlog += Mathf.Min(delta * hud.Speed, CharacterState.Step * MaxStepsPerFrame);
            while (backlog >= CharacterState.Step)
            {
                tickTimer.Restart();
                trial.Tick();
                tickTimer.Stop();
                tickMs = tickTimer.Elapsed.TotalMilliseconds;
                backlog -= CharacterState.Step;
            }

            ecology.SynchronizeVisuals();
            UpdateSettlement(delta);

            if (displayedDay != trial.Day)
            {
                displayedDay = trial.Day;
                terrainColor.SetColor("_BaseColor", trial.Ecology.GroundColor);
                terrain.SetPropertyBlock(terrainColor);
            }

            float daylight = Mathf.Clamp01(Mathf.Sin((trial.Hour - 6) * Mathf.PI / 12f));
            sun.transform.rotation = Quaternion.Euler((trial.Hour - 6) * 15f, 145, 0);
            sun.intensity = Mathf.Lerp(0.05f, 1.35f, daylight);
            sun.color = Color.Lerp(new Color(0.53f, 0.65f, 1), new Color(1, 0.96f, 0.84f), daylight);
            if (overview != null)
                overview.backgroundColor = Color.Lerp(new Color(0.035f, 0.055f, 0.12f), new Color(0.45f, 0.69f, 0.85f), daylight);

            hud.Refresh(trial, delta, tickMs);
            cameraControls?.Update();
            buildingPlacement.Update(hud);
            hud.RefreshBuildingPalette();
        }

        private void UpdateSettlement(float delta)
        {
            if (displayedGeneration != trial.Generation)
            {
                foreach (GameObject hut in huts)
                    Destroy(hut);
                huts.Clear();
                displayedGeneration = trial.Generation;
            }

            while (huts.Count < trial.Housing.Huts.Count)
            {
                var hut = trial.Housing.Huts[huts.Count];
                huts.Add(PlaceModel(hutPrefab, $"Hut {huts.Count + 1}", hut.Center, 2.7f));
            }

            foreach (var entry in characters)
            {
                if (!entry.Key.IsDead)
                    continue;
                Destroy(entry.Value.gameObject);
                departed.Add(entry.Key);
            }
            foreach (CharacterState person in departed)
                characters.Remove(person);
            departed.Clear();

            CharacterState selected = hud.SelectedPerson(trial);
            perceptionRing.enabled = hud.IsCharacterDebugVisible && selected != null;
            foreach (CharacterState person in trial.People)
            {
                if (!characters.TryGetValue(person, out Transform actor))
                {
                    actor = CreateCharacterView(person);
                    characters.Add(person, actor);
                }

                Vector2 point = Vector2.Lerp(person.PreviousPosition, person.Position, backlog / CharacterState.Step);
                actor.localPosition = new Vector3(point.x, 0f, point.y);
                actor.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, person.Age / PopulationSystem.AdultAge);
                Vector2 movement = person.TravelDirection;
                if (movement.sqrMagnitude > 0.000001f)
                {
                    Quaternion heading = Quaternion.LookRotation(new Vector3(movement.x, 0f, movement.y));
                    actor.localRotation = Quaternion.RotateTowards(actor.localRotation, heading, TurnDegreesPerSecond * delta * hud.Speed);
                }
                actor.gameObject.SetActive(person.Action != "Sleeping" || person.Hut == null);
                if (person == selected)
                    perceptionRing.transform.position = new Vector3(point.x, 0.08f, point.y);
            }
        }

        private Transform CreateCharacterView(CharacterState person)
        {
            var actor = new GameObject(person.Name);
            actor.transform.SetParent(transform, false);
            Transform body = PlaceModel(bodyPrefab, "Body", Vector2.zero, 1.65f).transform;
            body.SetParent(actor.transform, false);
            body.RotateAround(actor.transform.position, Vector3.up, bodyYawOffset);
            Color color = person.Id == 0 ? new Color(1f, 0.72f, 0.25f) : person.Id == 1
                ? new Color(0.65f, 0.55f, 1f) : new Color(0.3f, 0.85f, 0.72f);
            Shape("Resident Marker", PrimitiveType.Cylinder, Vector2.zero, 0.015f, new Vector3(1f, 0.03f, 1f), color)
                .transform.SetParent(actor.transform, false);
            return actor.transform;
        }

        private void OnDestroy()
        {
            cameraControls?.Dispose();
            buildingPlacement?.Dispose();
            if (ecology != null)
                ecology.DisposeRuntime();
            trial?.Dispose();
            Destroy(material);
            Destroy(gridOverlayMaterial);
            Destroy(gridOverlayMesh);
            hud?.Dispose();
        }
    }
}


