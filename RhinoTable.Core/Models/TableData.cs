using System.Text.Json;
using System.Text.Json.Serialization;

namespace RhinoTable.Core.Models
{
    public class TableData
    {
        public List<TableRowData> Rows { get; set; } = new();
        public List<double> ColumnWidths { get; set; } = new();
        public List<double> RowHeights { get; set; } = new();
        public string DefaultFontName { get; set; } = "Arial";
        public double DefaultFontSize { get; set; } = 3.5;
        public Guid? SourceObjectId { get; set; }
        // Pad naar het gekoppelde Excel-bestand; null = geen koppeling
        public string? LinkedExcelPath { get; set; }
        // Naam van het Rhino-blok; null = automatisch gegenereerde naam
        public string? TableName { get; set; }

        public static TableData CreateEmpty(int rows, int cols)
        {
            var table = new TableData();
            for (int c = 0; c < cols; c++) table.ColumnWidths.Add(30.0);
            for (int r = 0; r < rows; r++)
            {
                table.RowHeights.Add(8.0);
                var row = new TableRowData();
                for (int c = 0; c < cols; c++)
                    row.Cells.Add(new TableCellData());
                table.Rows.Add(row);
            }
            return table;
        }

        public string Serialize()
        {
            var options = new JsonSerializerOptions { WriteIndented = false };
            return JsonSerializer.Serialize(this, options);
        }

        public static TableData? Deserialize(string json)
        {
            return JsonSerializer.Deserialize<TableData>(json);
        }
    }
}
