using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using RhinoTable.Core.Import;
using RhinoTable.Core.Layout;
using RhinoTable.Core.Models;
using System.IO;

namespace Freeks_table_plugin.Commands
{
    // TableSync: doorloopt alle blokkendefinities in het document, zoekt naar
    // RhinoTable-blokken met een gekoppeld Excel-bestand en herlaadt ze ter plekke.
    public class TableSyncCommand : Command
    {
        public TableSyncCommand() { Instance = this; }
        public static TableSyncCommand Instance { get; private set; } = null!;
        public override string EnglishName => "TableSync";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            int updated = 0;
            int skipped = 0;

            var drawer = new RhinoTableDrawer();

            foreach (var idef in doc.InstanceDefinitions)
            {
                if (idef.IsDeleted) continue;

                string json = idef.Description ?? string.Empty;
                if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
                    continue;

                var tableData = TableData.Deserialize(json);
                if (tableData?.LinkedExcelPath == null) continue;

                string path = tableData.LinkedExcelPath;
                if (!File.Exists(path))
                {
                    RhinoApp.WriteLine($"  ⚠  Gekoppeld bestand niet gevonden: {path}");
                    skipped++;
                    continue;
                }

                RhinoApp.WriteLine($"  → Vernieuwen: {idef.Name}  ←  {Path.GetFileName(path)}");

                // Importeer vers Excel-bestand (gebruik opgeslagen werkbladnaam)
                TableData fresh;
                try   { fresh = new ExcelImporter().Import(path, tableData.LinkedExcelSheet); }
                catch (Exception ex) { RhinoApp.WriteLine($"     Fout: {ex.Message}"); skipped++; continue; }

                fresh.LinkedExcelPath  = path;
                fresh.LinkedExcelSheet = tableData.LinkedExcelSheet;

                // Pas alle instanties bij via de definitie-index
                var (geoms, attrs) = drawer.BuildGeometry(fresh);
                doc.InstanceDefinitions.ModifyGeometry(idef.Index, geoms, attrs);
                doc.InstanceDefinitions.Modify(idef, idef.Name, fresh.Serialize(), true);
                updated++;
            }

            doc.Views.Redraw();

            if (updated == 0 && skipped == 0)
                RhinoApp.WriteLine("TableSync: geen gekoppelde tabellen gevonden.");
            else
                RhinoApp.WriteLine($"TableSync klaar — {updated} bijgewerkt, {skipped} overgeslagen.");

            return Result.Success;
        }
    }
}
