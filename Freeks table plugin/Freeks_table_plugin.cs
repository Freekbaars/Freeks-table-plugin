using Rhino;
using Rhino.DocObjects;
using Rhino.PlugIns;
using RhinoTable.Core.Import;
using RhinoTable.Core.Layout;
using RhinoTable.Core.Models;
using RhinoTable.Core.Settings;
using RhinoTable.UI.Views;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Freeks_table_plugin
{
    public class Freeks_table_plugin : PlugIn
    {
        // Actieve file-watchers: Excel-pad → watcher
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        // Debounce: voorkomt dubbele triggers als Excel snel achter elkaar schrijft
        private readonly Dictionary<string, DateTime> _lastFired = new(StringComparer.OrdinalIgnoreCase);

        public Freeks_table_plugin()
        {
            Instance = this;
        }

        public static Freeks_table_plugin Instance { get; private set; } = null!;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // Scan huidig document op gekoppelde tabellen en zet watchers op
            RhinoDoc.EndOpenDocument  += (_, e) => ScanAndWatch(e.Document);
            RhinoDoc.NewDocument      += (_, e) => ScanAndWatch(e.Document);
            RhinoDoc.AddRhinoObject   += (_, e) => TryWatchInstance(e.TheObject, e.TheObject.Document);

            if (RhinoDoc.ActiveDoc != null)
                ScanAndWatch(RhinoDoc.ActiveDoc);

            _ = CheckForUpdateAsync();

            RhinoApp.WriteLine("RhinoTable loaded. Use: TableCreate, TableEdit, TableSync");
            return LoadReturnCode.Success;
        }

        private static async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            // Wacht kort zodat Rhino volledig geladen is voordat we een popup tonen
            await System.Threading.Tasks.Task.Delay(3000);

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var (hasUpdate, latestVersion) = await UpdateChecker.CheckAsync(currentVersion);

            if (!hasUpdate) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var dlg = new UpdateNotificationWindow(
                    currentVersion.ToString(3), latestVersion);
                dlg.Show();
            });
        }

        // ── Watcher beheer ────────────────────────────────────────────────────

        private void ScanAndWatch(RhinoDoc doc)
        {
            if (doc == null) return;
            foreach (var idef in doc.InstanceDefinitions)
            {
                if (idef.IsDeleted) continue;
                var table = TryGetLinkedTable(idef);
                if (table?.LinkedExcelPath != null)
                    EnsureWatching(table.LinkedExcelPath, doc);
            }
        }

        private void TryWatchInstance(RhinoObject obj, RhinoDoc doc)
        {
            if (obj is not InstanceObject inst) return;
            var table = TryGetLinkedTable(inst.InstanceDefinition);
            if (table?.LinkedExcelPath != null)
                EnsureWatching(table.LinkedExcelPath, doc);
        }

        private void EnsureWatching(string excelPath, RhinoDoc doc)
        {
            if (_watchers.ContainsKey(excelPath)) return;
            if (!File.Exists(excelPath)) return;

            string dir  = Path.GetDirectoryName(excelPath)!;
            string file = Path.GetFileName(excelPath);

            var watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter       = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, _) => OnExcelChanged(excelPath, doc);
            _watchers[excelPath] = watcher;
        }

        private void OnExcelChanged(string path, RhinoDoc doc)
        {
            // Debounce: negeer events die binnen 2 seconden op elkaar volgen
            var now = DateTime.UtcNow;
            if (_lastFired.TryGetValue(path, out var last) && (now - last).TotalSeconds < 2)
                return;
            _lastFired[path] = now;

            // Sync op de UI-thread uitvoeren
            RhinoApp.InvokeOnUiThread(() => SyncFile(path, doc));
        }

        private static void SyncFile(string path, RhinoDoc doc)
        {
            if (!File.Exists(path)) return;

            var drawer = new RhinoTableDrawer();
            int synced = 0;

            // Importeer per uniek werkblad zodat tabellen met verschillende werkbladen
            // elk de juiste data krijgen.
            var importCache = new Dictionary<string, TableData?>(StringComparer.OrdinalIgnoreCase);

            foreach (var idef in doc.InstanceDefinitions)
            {
                if (idef.IsDeleted) continue;
                var td = TryGetLinkedTable(idef);
                if (td?.LinkedExcelPath == null ||
                    !string.Equals(td.LinkedExcelPath, path, StringComparison.OrdinalIgnoreCase))
                    continue;

                string cacheKey = td.LinkedExcelSheet ?? string.Empty;
                if (!importCache.TryGetValue(cacheKey, out var fresh))
                {
                    try   { fresh = new ExcelImporter().Import(path, td.LinkedExcelSheet); }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine($"RhinoTable: failed to sync '{System.IO.Path.GetFileName(path)}': {ex.Message}");
                        fresh = null;
                    }
                    importCache[cacheKey] = fresh;
                }
                if (fresh == null) continue;

                fresh.LinkedExcelPath  = path;
                fresh.LinkedExcelSheet = td.LinkedExcelSheet;

                var (geoms, attrs) = drawer.BuildGeometry(fresh);
                doc.InstanceDefinitions.ModifyGeometry(idef.Index, geoms, attrs);
                doc.InstanceDefinitions.Modify(idef, idef.Name, fresh.Serialize(), true);
                synced++;
            }

            if (synced > 0)
            {
                doc.Views.Redraw();
                RhinoApp.WriteLine($"RhinoTable: {Path.GetFileName(path)} changed — {synced} table(s) updated.");
            }
        }

        private static TableData? TryGetLinkedTable(InstanceDefinition idef)
        {
            string json = idef.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
                return null;
            return TableData.Deserialize(json);
        }
    }
}
