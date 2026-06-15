using ClosedXML.Excel;
using RhinoTable.Core.Models;
using System.IO;

namespace RhinoTable.Core.Import
{
    public class ExcelImporter
    {
        public const int MaxRows = 500;
        public const int MaxCols = 100;

        public TableData Import(string filePath, IProgress<int>? progress = null)
        {
            // FileShare.ReadWrite zodat het bestand ook gelezen kan worden
            // terwijl Excel (of een ander programma) het nog open heeft.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var ws = wb.Worksheets.First();

            // RangeUsed() is betrouwbaarder dan LastRowUsed()/LastColumnUsed()
            // voor bestanden met lege rijen tussendoor of ongebruikte opmaak.
            var used = ws.RangeUsed();
            if (used == null) return TableData.CreateEmpty(3, 4);

            int firstRow = used.FirstRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastRow  = Math.Min(used.LastRow().RowNumber(),    firstRow + MaxRows - 1);
            int lastCol  = Math.Min(used.LastColumn().ColumnNumber(), firstCol + MaxCols - 1);

            int rowCount = lastRow - firstRow + 1;
            int colCount = lastCol - firstCol + 1;

            var table = new TableData();

            for (int c = firstCol; c <= lastCol; c++)
            {
                double w = ws.Column(c).Width;
                // Excel breedte is in tekeneenheden (~7px per eenheid bij 96dpi),
                // ruwweg 2.54 mm per eenheid geeft bruikbare Rhino-maten.
                table.ColumnWidths.Add(w > 0 ? Math.Round(w * 2.54, 1) : 30.0);
            }

            for (int r = firstRow; r <= lastRow; r++)
            {
                double h = ws.Row(r).Height;
                // Excel rijhoogte is in punten (1pt = 0.353mm).
                table.RowHeights.Add(h > 0 ? Math.Round(h * 0.353, 1) : 8.0);

                var row = new TableRowData();
                for (int c = firstCol; c <= lastCol; c++)
                {
                    var xlCell = ws.Cell(r, c);

                    // Achtergrondkleur
                    string? bgHex = null;
                    var fill = xlCell.Style.Fill;
                    if (fill.PatternType != XLFillPatternValues.None
                        && fill.BackgroundColor.ColorType == XLColorType.Color)
                    {
                        var col = fill.BackgroundColor.Color;
                        // Witte achtergrond overslaan — dat is de standaardkleur.
                        if (!(col.R == 255 && col.G == 255 && col.B == 255))
                            bgHex = $"{col.R:X2}{col.G:X2}{col.B:X2}";
                    }

                    // Tekstkleur
                    string? fgHex = null;
                    var fc = xlCell.Style.Font.FontColor;
                    if (fc.ColorType == XLColorType.Color)
                    {
                        var col = fc.Color;
                        // Zwart overslaan — dat is de standaardkleur.
                        if (!(col.R == 0 && col.G == 0 && col.B == 0))
                            fgHex = $"{col.R:X2}{col.G:X2}{col.B:X2}";
                    }

                    // Uitlijning
                    var hAlign = xlCell.Style.Alignment.Horizontal switch
                    {
                        XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                        XLAlignmentHorizontalValues.Right  => HorizontalAlignment.Right,
                        _                                  => HorizontalAlignment.Left
                    };

                    row.Cells.Add(new TableCellData
                    {
                        Text                = xlCell.Value.ToString() ?? string.Empty,
                        Bold                = xlCell.Style.Font.Bold,
                        Italic              = xlCell.Style.Font.Italic,
                        HorizontalAlignment = hAlign,
                        BackgroundColor     = bgHex,
                        // FillPattern 1 = effen — zodat de kleur ook zichtbaar is.
                        FillPattern         = bgHex != null ? 1 : 0,
                        TextColor           = fgHex,
                    });
                }
                table.Rows.Add(row);
                progress?.Report(r - firstRow + 1);
            }

            return table;
        }
    }
}
