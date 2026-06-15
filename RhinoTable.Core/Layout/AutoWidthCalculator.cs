using RhinoTable.Core.Models;

namespace RhinoTable.Core.Layout
{
    public class AutoWidthCalculator
    {
        public void Apply(TableData table, double charWidthMm = 2.2, double paddingMm = 4.0)
        {
            for (int c = 0; c < table.ColumnWidths.Count; c++)
            {
                double maxWidth = paddingMm;
                foreach (var row in table.Rows)
                {
                    if (c >= row.Cells.Count) continue;
                    var cell = row.Cells[c];
                    if (cell.IsMergedHidden) continue;

                    var lines = cell.Text.Split('\n');
                    int maxLen = lines.Max(l => l.Length);
                    double w = maxLen * charWidthMm + paddingMm;
                    if (w > maxWidth) maxWidth = w;
                }
                table.ColumnWidths[c] = maxWidth;
            }
        }
    }
}
