using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using RhinoTable.Core.Models;
using RhinoTable.UI.Views;

namespace Freeks_table_plugin.Commands
{
    public class TableEditCommand : Command
    {
        public TableEditCommand() { Instance = this; }
        public static TableEditCommand Instance { get; private set; } = null!;
        public override string EnglishName => "TableEdit";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Selecteer een RhinoTable blok om te bewerken");
            go.GeometryFilter = ObjectType.InstanceReference;
            go.Get();

            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            if (go.Object(0).Object() is not InstanceObject instanceObj)
            {
                RhinoApp.WriteLine("Het geselecteerde object is geen blok.");
                return Result.Failure;
            }

            // Table JSON is stored in the InstanceDefinition description
            string json = instanceObj.InstanceDefinition.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            {
                RhinoApp.WriteLine("Dit blok is geen RhinoTable (geen tabeldata gevonden).");
                return Result.Failure;
            }

            var tableData = TableData.Deserialize(json);
            if (tableData == null)
            {
                RhinoApp.WriteLine("Tabeldata kon niet worden gelezen.");
                return Result.Failure;
            }

            tableData.SourceObjectId = instanceObj.Id;

            var window = new TableEditorWindow(doc, tableData);
            window.Show();
            return Result.Success;
        }
    }
}
