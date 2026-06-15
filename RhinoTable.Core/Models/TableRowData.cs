namespace RhinoTable.Core.Models
{
    public class TableRowData
    {
        public List<TableCellData> Cells { get; set; } = new();
        public bool IsHeader { get; set; } = false;
    }
}
