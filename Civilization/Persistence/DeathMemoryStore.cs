using UnityEngine;

namespace Civilization
{
    [System.Serializable]
    internal struct DeathMemory
    {
        public int HungerDeaths;
        public int ThirstDeaths;
        public string Cause;
        public string Action;
        public bool HadKnownSupply;
    }

    internal static class DeathMemoryStore
    {
        private const string FirstAdultKey = "Civilization.SurvivalTrial.Deaths.v1";

        private static string Key(int index) => index switch
        {
            0 => FirstAdultKey,
            1 => FirstAdultKey + ".AdultB",
            _ => FirstAdultKey + "." + index
        };

        internal static DeathMemory Load(int index)
        {
            string saved = PlayerPrefs.GetString(Key(index));
            return string.IsNullOrEmpty(saved) ? default : JsonUtility.FromJson<DeathMemory>(saved);
        }

        internal static void Save(int index, DeathMemory memory)
        {
            PlayerPrefs.SetString(Key(index), JsonUtility.ToJson(memory));
            PlayerPrefs.Save();
        }
    }
}
