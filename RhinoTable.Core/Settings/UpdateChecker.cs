using System.Net.Http;
using System.Text.Json;

namespace RhinoTable.Core.Settings
{
    public static class UpdateChecker
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private const string SearchUrl = "https://yak.rhino3d.com/packages?search=rhinotable";

        private static readonly string _stateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RhinoTable", "update_check.txt");

        public static async Task<(bool HasUpdate, string LatestVersion)> CheckAsync(Version currentVersion)
        {
            try
            {
                if (!ShouldCheck()) return (false, string.Empty);

                var json = await _http.GetStringAsync(SearchUrl);
                SaveCheckTime();

                var latest = FindLatestVersion(json);
                if (latest == null) return (false, string.Empty);

                if (Version.TryParse(latest, out var latestVer) && latestVer > currentVersion)
                    return (true, latest);

                return (false, latest);
            }
            catch
            {
                return (false, string.Empty);
            }
        }

        // Parst de JSON en pakt alleen versies uit objecten waar "name" == "rhinotable".
        // Daarmee worden versies van afhankelijkheden (ClosedXML, OpenXML, etc.) genegeerd.
        private static string? FindLatestVersion(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var versions = new List<Version>();
                CollectVersions(doc.RootElement, versions);
                return versions.OrderByDescending(v => v).FirstOrDefault()?.ToString();
            }
            catch { return null; }
        }

        private static void CollectVersions(JsonElement el, List<Version> versions)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    CollectVersions(item, versions);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("name", out var nameEl) &&
                    string.Equals(nameEl.GetString(), "rhinotable", StringComparison.OrdinalIgnoreCase) &&
                    el.TryGetProperty("version", out var verEl) &&
                    Version.TryParse(verEl.GetString(), out var ver))
                {
                    versions.Add(ver);
                }

                foreach (var prop in el.EnumerateObject())
                    CollectVersions(prop.Value, versions);
            }
        }

        private static bool ShouldCheck()
        {
            try
            {
                if (!File.Exists(_stateFile)) return true;
                var last = DateTime.Parse(File.ReadAllText(_stateFile).Trim());
                return (DateTime.UtcNow - last).TotalHours >= 24;
            }
            catch { return true; }
        }

        private static void SaveCheckTime()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
                File.WriteAllText(_stateFile, DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }
    }
}
