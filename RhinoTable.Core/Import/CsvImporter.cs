using CsvHelper;
using CsvHelper.Configuration;
using RhinoTable.Core.Models;
using System.Globalization;

namespace RhinoTable.Core.Import
{
    public class CsvImporter
    {
        public const int MaxRows = 500;
        public const int MaxCols = 100;

        public TableData Import(string filePath, IProgress<int>? progress = null)
        {
            char sep = DetectSeparator(filePath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord  = false,
                MissingFieldFound = null,
                Delimiter        = sep.ToString(),
            };

            var records = new List<List<string>>();

            using (var reader = new StreamReader(filePath))
            using (var csv    = new CsvReader(reader, config))
            {
                while (csv.Read())
                {
                    if (records.Count >= MaxRows) break;

                    var row = new List<string>();
                    for (int i = 0; i < MaxCols && csv.TryGetField<string>(i, out var val); i++)
                        row.Add(val ?? string.Empty);
                    records.Add(row);

                    if (records.Count % 50 == 0)
                        progress?.Report(records.Count);
                }
            }

            int colCount = records.Count > 0 ? records.Max(r => r.Count) : 1;

            var table = new TableData();
            for (int c = 0; c < colCount; c++) table.ColumnWidths.Add(30.0);

            int ri = 0;
            foreach (var record in records)
            {
                table.RowHeights.Add(8.0);
                var row = new TableRowData();
                for (int c = 0; c < colCount; c++)
                    row.Cells.Add(new TableCellData { Text = c < record.Count ? record[c] : string.Empty });
                table.Rows.Add(row);
                progress?.Report(++ri);
            }

            return table;
        }

        // Detecteert het scheidingsteken door de eerste regels te samplen.
        // Kiest het teken dat het meest consistent (gelijke kolommen per rij)
        // en het vaakst voorkomt.
        private static char DetectSeparator(string filePath)
        {
            var candidates = new[] { ',', ';', '|', '\t' };
            var counts     = candidates.ToDictionary(c => c, _ => new List<int>());

            using var reader = new StreamReader(filePath);
            int linesRead = 0;
            while (linesRead < 20)
            {
                string? line = reader.ReadLine();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                foreach (char sep in candidates)
                    counts[sep].Add(line.Count(ch => ch == sep));

                linesRead++;
            }

            char best      = ',';
            double bestScore = -1;

            foreach (char sep in candidates)
            {
                var vals = counts[sep].Where(v => v > 0).ToList();
                if (vals.Count == 0) continue;

                double avg      = vals.Average();
                double variance = vals.Count > 1
                    ? vals.Select(v => Math.Pow(v - avg, 2)).Average()
                    : 0;

                // Hoge gemiddelde kolommen + lage variantie = meest stabiele separator
                double score = avg / (1.0 + variance);
                if (score > bestScore)
                {
                    bestScore = score;
                    best      = sep;
                }
            }

            return best;
        }
    }
}
