using UnityEngine;

namespace Civilization.Ecology
{
    internal enum LeaflessSeason { None, Spring, Summer, Autumn, Winter }

    [CreateAssetMenu(menuName = "Civilization/Ecosystem/Plant Profile")]
    public sealed class PlantProfile : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [Tooltip("Height in world units for the mature bush or leafy tree. Leafless trees keep their relative height.")]
        [SerializeField, Min(0.1f)] private float matureHeight = 1f;
        [SerializeField, Range(0.12f, 1f)] private float minimumStartingGrowth = 0.5f;
        [SerializeField, Min(0f)] private float dailyGrowth = 0.03f;
        [SerializeField, Min(1)] private int minimumLifespan = 95;
        [SerializeField, Min(1)] private int maximumLifespan = 169;

        [Header("Lifecycle")]
        [SerializeField, Min(1)] private int maturityDays = 36;
        [SerializeField, Min(1)] private int minimumSeedDormancy = 12;
        [SerializeField, Min(1)] private int maximumSeedDormancy = 28;

        [Header("Foliage")]
        [Tooltip("Optional prefab used when the tree sheds its foliage.")]
        [SerializeField] private GameObject leaflessPrefab;
        [SerializeField] private LeaflessSeason shedInSeason;
        [Tooltip("The tree remains leafless for its final age days. Zero disables age-based shedding.")]
        [SerializeField, Min(0f)] private float leaflessDaysBeforeDeath;

        [Header("Fruit")]
        [SerializeField] private GameObject fruitPrefab;
        [SerializeField, Min(0)] private int fruitServings;
        [SerializeField, Min(0.05f)] private float fruitHeight = 0.24f;

        internal GameObject Prefab => prefab;
        internal float MatureHeight => matureHeight;
        internal float MinimumStartingGrowth => minimumStartingGrowth;
        internal float DailyGrowth => dailyGrowth;
        internal int MinimumLifespan => Mathf.Max(1, minimumLifespan);
        internal int MaximumLifespan => Mathf.Max(MinimumLifespan, maximumLifespan);
        internal int MaturityDays => maturityDays;
        internal int MinimumSeedDormancy => minimumSeedDormancy;
        internal int MaximumSeedDormancy => Mathf.Max(minimumSeedDormancy, maximumSeedDormancy);
        internal GameObject LeaflessPrefab => leaflessPrefab;

        internal int ShedSeason => (int)shedInSeason - 1;
        internal float LeaflessDaysBeforeDeath => leaflessDaysBeforeDeath;

        internal GameObject FruitPrefab => fruitPrefab;
        internal int FruitServings => fruitPrefab != null ? fruitServings : 0;
        internal float FruitHeight => fruitHeight;
    }
}
