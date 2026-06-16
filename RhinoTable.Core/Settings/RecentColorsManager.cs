using System.Text.Json;

namespace RhinoTable.Core.Settings
{
    public static class RecentColorsManager
    {
        private static readonly string _file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RhinoTable", "recent_colors.json");

        private const int MaxColors = 12;

        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(_file)) return new List<string>();
                var json = File.ReadAllText(_file);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public static List<string> Add(string hex, List<string> current)
        {
            if (string.IsNullOrWhiteSpace(hex)) return current;
            hex = hex.Trim().ToUpperInvariant();
            var list = new List<string>(current);
            list.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, hex);
            if (list.Count > MaxColors) list.RemoveRange(MaxColors, list.Count - MaxColors);
            return list;
        }

        public static void Save(IEnumerable<string> colors)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(colors.ToList()));
            }
            catch { }
        }
    }
}
