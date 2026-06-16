using RhinoTable.Core.Models;
using System.Text.Json;

namespace RhinoTable.Core.Settings
{
    public class TableTemplate
    {
        public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool   IsBuiltIn   { get; set; }
        public TableData TableData { get; set; } = new();
    }

    public static class TemplateManager
    {
        private static readonly string _templateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RhinoTable", "templates");

        private static readonly JsonSerializerOptions _jsonOpts =
            new() { WriteIndented = true };

        public static List<TableTemplate> LoadAll()
        {
            var result = new List<TableTemplate>(CreateBuiltIns());

            if (!Directory.Exists(_templateDir)) return result;

            foreach (var file in Directory.GetFiles(_templateDir, "*.json").OrderBy(f => f))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<TableTemplate>(File.ReadAllText(file));
                    if (t != null) { t.IsBuiltIn = false; result.Add(t); }
                }
                catch { }
            }
            return result;
        }

        public static void Save(TableTemplate template)
        {
            Directory.CreateDirectory(_templateDir);
            template.IsBuiltIn = false;
            File.WriteAllText(SafePath(template.Name), JsonSerializer.Serialize(template, _jsonOpts));
        }

        public static void Delete(TableTemplate template)
        {
            if (template.IsBuiltIn) return;
            var path = SafePath(template.Name);
            if (File.Exists(path)) File.Delete(path);
        }

        public static TableData CloneData(TableData src)
            => TableData.Deserialize(src.Serialize())!;

        private static string SafePath(string name)
        {
            var safe = string.Concat(name.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(_templateDir, safe + ".json");
        }

        // ── Ingebouwde sjablonen ──────────────────────────────────────────────

        private static List<TableTemplate> CreateBuiltIns() => new()
        {
            CreateTitelblok(),
            CreateBom(),
            CreateRevisietabel()
        };

        private static TableTemplate CreateTitelblok()
        {
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 60, 40, 25, 25 },
                RowHeights      = Enumerable.Repeat(8.0, 6).ToList()
            };

            // Inhoud per rij [rij][kolom]
            string[][] texts =
            {
                new[] { "Project Name",  "Client",    "Date",   "Drawing No." },
                new[] { "",             "",           "",        ""            },
                new[] { "Description",  "",           "Scale",   "Revision"   },
                new[] { "",             "",           "",        ""            },
                new[] { "Designer",     "Checked by", "Format",  "Sheet"      },
                new[] { "",             "",           "A3",      "1 / 1"      },
            };
            bool[] header = { true, false, false, false, false, false };

            for (int r = 0; r < texts.Length; r++)
            {
                var row = new TableRowData { IsHeader = header[r] };
                for (int c = 0; c < 4; c++)
                {
                    bool isLabel = header[r] || (r == 2 && c == 0) || r == 4;
                    row.Cells.Add(new TableCellData
                    {
                        Text                = texts[r][c],
                        Bold                = isLabel,
                        FontSize            = header[r] ? 3.5 : 3.0,
                        BackgroundColor     = header[r] ? "#D6EAF8" : (r % 2 == 1 ? "#F0F4F8" : null),
                        BorderBottom        = 0.25f,
                        BorderTop           = header[r] ? 0.5f : 0f,
                        BorderLeft          = 0.25f,
                        BorderRight         = c == 3 ? 0.25f : 0f,
                        IsMergedHidden      = (r == 2 || r == 3) && c == 1,
                        MergeRight          = (r == 2 || r == 3) && c == 0 ? 1 : 0,
                    });
                }
                td.Rows.Add(row);
            }
            return new TableTemplate
            {
                Name        = "Drawing Title Block",
                Description = "Standard drawing title block with project name, client, scale, revision, and sheet number.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }

        private static TableTemplate CreateBom()
        {
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 12, 40, 55, 15, 30, 40 },
                RowHeights      = Enumerable.Repeat(8.0, 8).ToList()
            };

            string[] headers = { "No.", "Name", "Description", "Qty", "Material", "Remarks" };

            var hrow = new TableRowData { IsHeader = true };
            foreach (var h in headers)
                hrow.Cells.Add(new TableCellData
                {
                    Text            = h,
                    Bold            = true,
                    FontSize        = 3.5,
                    BackgroundColor = "#D6EAF8",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BorderBottom    = 0.5f,
                    BorderTop       = 0.5f,
                    BorderLeft      = 0.25f,
                    BorderRight     = 0.25f
                });
            td.Rows.Add(hrow);

            for (int r = 0; r < 7; r++)
            {
                var row = new TableRowData();
                for (int c = 0; c < 6; c++)
                    row.Cells.Add(new TableCellData
                    {
                        Text                = c == 0 ? (r + 1).ToString() : string.Empty,
                        HorizontalAlignment = c == 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                        BackgroundColor     = r % 2 == 1 ? "#F0F4F8" : null,
                        BorderBottom        = 0.25f,
                        BorderLeft          = 0.25f,
                        BorderRight         = 0.25f
                    });
                td.Rows.Add(row);
            }
            return new TableTemplate
            {
                Name        = "Bill of Materials (BOM)",
                Description = "Bill of Materials with number, name, description, quantity, material, and remarks.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }

        private static TableTemplate CreateRevisietabel()
        {
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 12, 65, 25, 20 },
                RowHeights      = Enumerable.Repeat(8.0, 5).ToList()
            };

            string[] headers = { "Rev.", "Description", "Date", "Init." };
            var hrow = new TableRowData { IsHeader = true };
            foreach (var h in headers)
                hrow.Cells.Add(new TableCellData
                {
                    Text            = h,
                    Bold            = true,
                    FontSize        = 3.5,
                    BackgroundColor = "#D6EAF8",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BorderBottom    = 0.5f,
                    BorderTop       = 0.5f,
                    BorderLeft      = 0.25f,
                    BorderRight     = 0.25f
                });
            td.Rows.Add(hrow);

            string[] revs = { "A", "B", "C", "D" };
            for (int r = 0; r < 4; r++)
            {
                var row = new TableRowData();
                foreach (var (align, i) in new[] {
                    HorizontalAlignment.Center,
                    HorizontalAlignment.Left,
                    HorizontalAlignment.Center,
                    HorizontalAlignment.Center
                }.Select((a, i) => (a, i)))
                {
                    row.Cells.Add(new TableCellData
                    {
                        Text                = i == 0 ? revs[r] : string.Empty,
                        HorizontalAlignment = align,
                        BorderBottom        = 0.25f,
                        BorderLeft          = 0.25f,
                        BorderRight         = 0.25f
                    });
                }
                td.Rows.Add(row);
            }
            return new TableTemplate
            {
                Name        = "Revision Table",
                Description = "Revision table for drawings with revision letter, description, date, and initials.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }
    }
}
