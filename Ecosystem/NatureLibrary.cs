using System;
using System.Collections.Generic;
using UnityEngine;

namespace Civilization.Ecology
{
    [CreateAssetMenu(menuName = "Civilization/Ecosystem/Nature Library")]
    public sealed class NatureLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class TreeCategory
        {
            [SerializeField] private string name = "New category";
            [SerializeField] private PlantProfile[] profiles = Array.Empty<PlantProfile>();

            public string Name => name;
            public IReadOnlyList<PlantProfile> Profiles => profiles;
        }

        [SerializeField] private TreeCategory[] treeCategories = Array.Empty<TreeCategory>();
        [SerializeField] private PlantProfile[] bushes = Array.Empty<PlantProfile>();
        [SerializeField] private GameObject[] grass = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] rocks = Array.Empty<GameObject>();

        public IReadOnlyList<TreeCategory> TreeCategories => treeCategories;
        public IReadOnlyList<PlantProfile> Bushes => bushes;
        public IReadOnlyList<GameObject> Grass => grass;
        public IReadOnlyList<GameObject> Rocks => rocks;

        internal void CollectPlantProfiles(List<PlantProfile> treeProfiles, List<PlantProfile> bushProfiles)
        {
            foreach (TreeCategory category in treeCategories)
                if (category != null)
                    AddProfiles(category.Profiles, treeProfiles);
            AddProfiles(bushes, bushProfiles);
        }

        private static void AddProfiles(IReadOnlyList<PlantProfile> source, List<PlantProfile> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                PlantProfile profile = source[i];
                if (profile != null && profile.Prefab != null
                    && profile.Prefab.GetComponentInChildren<Renderer>() != null && !target.Contains(profile))
                    target.Add(profile);
            }
        }
    }
}
