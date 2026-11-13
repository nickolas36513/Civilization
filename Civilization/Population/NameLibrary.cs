namespace Civilization
{
    internal static class NameLibrary
    {
        private static readonly string[] GivenNames =
        {
            "Ari", "Mira", "Nila", "Kiran", "Tara", "Ishan", "Anya", "Ravi",
            "Leela", "Dev", "Sana", "Veer", "Ila", "Neel", "Rhea", "Adi"
        };

        internal static string ForId(int id)
        {
            string name = GivenNames[id % GivenNames.Length];
            return id < GivenNames.Length ? name : $"{name} {1 + id / GivenNames.Length}";
        }
    }
}
