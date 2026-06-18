using ClosedXML.Excel;
using RhinoTable.Core.Models;
using System.IO;

namespace RhinoTable.Core.Import
{
    public class ExcelImporter
    {
        public const int MaxRows = 500;
        public const int MaxCols = 100;

        public static List<string> GetSheetNames(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            return wb.Worksheets.Select(ws => ws.Name).ToList();
        }

        public TableData Import(string filePath, string? sheetName = null, IProgress<int>? progress = null)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);

            var ws = sheetName != null
                ? (wb.Worksheets.FirstOrDefault(w => w.Name == sheetName) ?? wb.Worksheets.First())
                : wb.Worksheets.First();

            var used = ws.RangeUsed();
            if (used == null) return TableData.CreateEmpty(3, 4);

            int firstRow = used.FirstRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastRow  = Math.Min(used.LastRow().RowNumber(),    firstRow + MaxRows - 1);
            int lastCol  = Math.Min(used.LastColumn().ColumnNumber(), firstCol + MaxCols - 1);

            var table = new TableData();

            for (int c = firstCol; c <= lastCol; c++)
            {
                double w = ws.Column(c).Width;
                table.ColumnWidths.Add(w > 0 ? Math.Min(Math.Round(w * 2.54, 1), 80.0) : 30.0);
            }

            for (int r = firstRow; r <= lastRow; r++)
            {
                double h = ws.Row(r).Height;
                table.RowHeights.Add(h > 0 ? Math.Round(h * 0.353, 1) : 8.0);

                var row = new TableRowData();
                for (int c = firstCol; c <= lastCol; c++)
                {
                    var xlCell = ws.Cell(r, c);

                    // Formulecellen: gebruik de gecachede waarde om re-evaluatie te vermijden.
                    // ClosedXML gooit bij niet-ondersteunde functies een exception als je .Value gebruikt.
                    XLCellValue cellValue;
                    try
                    {
                        cellValue = xlCell.HasFormula ? xlCell.CachedValue : xlCell.Value;
                    }
                    catch
                    {
                        cellValue = xlCell.CachedValue;
                    }

                    string text;
                    try
                    {
                        text = (cellValue.IsBlank || cellValue.IsError)
                            ? string.Empty
                            : cellValue.ToString() ?? string.Empty;
                    }
                    catch { text = string.Empty; }

                    // Achtergrondkleur
                    string? bgHex = null;
                    var fill = xlCell.Style.Fill;
                    if (fill.PatternType != XLFillPatternValues.None
                        && fill.BackgroundColor.ColorType == XLColorType.Color)
                    {
                        var col = fill.BackgroundColor.Color;
                        if (!(col.R == 255 && col.G == 255 && col.B == 255))
                            bgHex = $"{col.R:X2}{col.G:X2}{col.B:X2}";
                    }

                    // Tekstkleur
                    string? fgHex = null;
                    var fc = xlCell.Style.Font.FontColor;
                    if (fc.ColorType == XLColorType.Color)
                    {
                        var col = fc.Color;
                        if (!(col.R == 0 && col.G == 0 && col.B == 0))
                            fgHex = $"{col.R:X2}{col.G:X2}{col.B:X2}";
                    }

                    var hAlign = xlCell.Style.Alignment.Horizontal switch
                    {
                        XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                        XLAlignmentHorizontalValues.Right  => HorizontalAlignment.Right,
                        _                                  => HorizontalAlignment.Left
                    };

                    row.Cells.Add(new TableCellData
                    {
                        Text                = text,
                        Bold                = xlCell.Style.Font.Bold,
                        Italic              = xlCell.Style.Font.Italic,
                        HorizontalAlignment = hAlign,
                        BackgroundColor     = bgHex,
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
