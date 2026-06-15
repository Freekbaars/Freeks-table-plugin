using Rhino;
using Rhino.Commands;
using RhinoTable.Core.Models;
using RhinoTable.UI.Views;

namespace Freeks_table_plugin.Commands
{
    public class TableCreateCommand : Command
    {
        public TableCreateCommand() { Instance = this; }
        public static TableCreateCommand Instance { get; private set; } = null!;
        public override string EnglishName => "TableCreate";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Open the editor with a default 3×4 table; user adds/removes rows via toolbar
            var tableData = TableData.CreateEmpty(rows: 3, cols: 4);
            var window = new TableEditorWindow(doc, tableData);
            window.Show();
            return Result.Success;
        }
    }
}
