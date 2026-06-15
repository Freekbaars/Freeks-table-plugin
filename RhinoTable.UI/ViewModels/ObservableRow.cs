using RhinoTable.Core.Models;
using System.Collections.ObjectModel;

namespace RhinoTable.UI.ViewModels
{
    public class ObservableRow
    {
        public ObservableCollection<TableCellData> Cells { get; }
        public int RowIndex { get; }
        public bool IsHeader { get; }

        public ObservableRow(TableRowData row, int index)
        {
            Cells    = new ObservableCollection<TableCellData>(row.Cells);
            RowIndex = index;
            IsHeader = row.IsHeader;
        }
    }
}
