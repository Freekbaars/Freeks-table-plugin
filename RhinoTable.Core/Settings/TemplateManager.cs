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
            CreateRevisietabel(),
            CreateRoomSchedule(),
            CreateMaterialLegend(),
            CreateCoordinateTable()
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

        private static TableTemplate CreateRoomSchedule()
        {
            // 7 columns: No. | Room Name | Area | Floor | Wall | Ceiling | Remarks
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 12, 42, 20, 30, 30, 30, 36 },
                RowHeights      = Enumerable.Repeat(8.0, 9).ToList()
            };
            td.RowHeights[0] = 11.0;

            // Row 0: merged title spanning all 7 columns
            var titleRow = new TableRowData();
            titleRow.Cells.Add(new TableCellData
            {
                Text                = "ROOM SCHEDULE",
                Bold                = true,
                FontSize            = 5.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Middle,
                BackgroundColor     = "#1A3A5C",
                TextColor           = "#FFFFFF",
                MergeRight          = 6,
                BorderTop           = 0.5f,
                BorderBottom        = 0.5f,
                BorderLeft          = 0.5f,
                BorderRight         = 0.5f
            });
            for (int c = 1; c < 7; c++)
                titleRow.Cells.Add(new TableCellData { IsMergedHidden = true });
            td.Rows.Add(titleRow);

            // Row 1: column headers — "Area (m^{2})" uses superscript syntax
            string[] colHeaders = { "No.", "Room Name", "Area (m^{2})", "Floor Finish", "Wall Finish", "Ceiling Finish", "Remarks" };
            var hrow = new TableRowData { IsHeader = true };
            foreach (var h in colHeaders)
                hrow.Cells.Add(new TableCellData
                {
                    Text                = h,
                    Bold                = true,
                    FontSize            = 3.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BackgroundColor     = "#2471A3",
                    TextColor           = "#FFFFFF",
                    BorderTop           = 0.25f,
                    BorderBottom        = 0.5f,
                    BorderLeft          = 0.25f,
                    BorderRight         = 0.25f
                });
            td.Rows.Add(hrow);

            // 7 data rows
            string[][] data =
            {
                new[] { "1.01", "Reception",    "42.5",  "Granite tiles", "Paint",       "Plaster",    ""               },
                new[] { "1.02", "Office",        "28.0",  "Carpet",        "Paint",       "T-ceiling",  ""               },
                new[] { "1.03", "Meeting Room",  "35.8",  "Parquet",       "Plaster",     "T-ceiling",  "AV equipment"   },
                new[] { "1.04", "Pantry",        "12.0",  "Tiles",         "Tiles",       "Plaster",    ""               },
                new[] { "1.05", "Toilet",        "8.5",   "Tiles",         "Tiles",       "Plaster",    ""               },
                new[] { "1.06", "Storage",       "6.0",   "Concrete",      "Paint",       "Open",       "Shelving unit"  },
                new[] { "2.01", "Open Office",   "120.0", "Raised floor",  "Glazing",     "T-ceiling",  "Open floor plan"},
            };

            for (int r = 0; r < data.Length; r++)
            {
                var row = new TableRowData();
                string? altBg = r % 2 == 1 ? "#EBF5FB" : null;
                for (int c = 0; c < 7; c++)
                    row.Cells.Add(new TableCellData
                    {
                        Text                = data[r][c],
                        HorizontalAlignment = (c == 0 || c == 2) ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                        BackgroundColor     = altBg,
                        BorderBottom        = 0.25f,
                        BorderLeft          = 0.25f,
                        BorderRight         = 0.25f
                    });
                td.Rows.Add(row);
            }

            return new TableTemplate
            {
                Name        = "Room Schedule",
                Description = "Architectural room schedule with area (m²), floor/wall/ceiling finishes. Showcases merged title row, superscript, and alternating row colours.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }

        private static TableTemplate CreateMaterialLegend()
        {
            // 4 columns: Pattern | Material | Reference | Description
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 22, 38, 32, 68 },
                RowHeights      = new List<double>  { 11, 8, 11, 11, 11, 11, 11 }
            };

            // Row 0: merged title spanning all 4 columns
            var titleRow = new TableRowData();
            titleRow.Cells.Add(new TableCellData
            {
                Text                = "MATERIAL LEGEND",
                Bold                = true,
                FontSize            = 5.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Middle,
                BackgroundColor     = "#1A5C3A",
                TextColor           = "#FFFFFF",
                MergeRight          = 3,
                BorderTop           = 0.5f,
                BorderBottom        = 0.5f,
                BorderLeft          = 0.5f,
                BorderRight         = 0.5f
            });
            for (int c = 1; c < 4; c++)
                titleRow.Cells.Add(new TableCellData { IsMergedHidden = true });
            td.Rows.Add(titleRow);

            // Row 1: column headers
            string[] colHeaders = { "Pattern", "Material", "Reference", "Description" };
            var hrow = new TableRowData { IsHeader = true };
            foreach (var h in colHeaders)
                hrow.Cells.Add(new TableCellData
                {
                    Text                = h,
                    Bold                = true,
                    FontSize            = 3.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BackgroundColor     = "#1E8449",
                    TextColor           = "#FFFFFF",
                    BorderTop           = 0.25f,
                    BorderBottom        = 0.5f,
                    BorderLeft          = 0.25f,
                    BorderRight         = 0.25f
                });
            td.Rows.Add(hrow);

            // Material rows: (hatchName, hatchColor, solidBg, material, reference, description)
            var materials = new (string? Hatch, string? HColor, string? Bg, string Mat, string Ref, string Desc)[]
            {
                ("Hatch1", "#4A4A4A", null,      "Concrete",    "NEN-EN 206",      "Reinforced concrete structural elements and foundations"),
                ("Hatch2", "#8B4513", null,      "Masonry",     "NEN-EN 1996",     "Load-bearing clay brick or concrete block masonry"),
                ("Hatch3", "#2C2C2C", null,      "Steel",       "NEN-EN 1993",     "Structural steel sections, plates and connections"),
                (null,     null,      "#D5F5E3", "Insulation",  "NEN-EN ISO 6946", "Thermal and acoustic insulation (rock wool / PIR)"),
                (null,     null,      "#FEF9E7", "Timber",      "NEN-EN 1995",     "Structural timber and engineered wood products (CLT / LVL)"),
            };

            foreach (var m in materials)
            {
                var row = new TableRowData();

                // Pattern cell — hatch fill or solid colour
                row.Cells.Add(new TableCellData
                {
                    HatchPatternName = m.Hatch,
                    HatchColor       = m.HColor,
                    HatchScale       = 1.0,
                    BackgroundColor  = m.Bg,
                    BorderBottom     = 0.25f,
                    BorderLeft       = 0.25f,
                    BorderRight      = 0.25f
                });
                // Material name — bold
                row.Cells.Add(new TableCellData
                {
                    Text            = m.Mat,
                    Bold            = true,
                    BackgroundColor = m.Bg,
                    BorderBottom    = 0.25f,
                    BorderLeft      = 0.25f,
                    BorderRight     = 0.25f
                });
                // Reference — italic, centered
                row.Cells.Add(new TableCellData
                {
                    Text                = m.Ref,
                    Italic              = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BackgroundColor     = m.Bg,
                    BorderBottom        = 0.25f,
                    BorderLeft          = 0.25f,
                    BorderRight         = 0.25f
                });
                // Description — word wrap
                row.Cells.Add(new TableCellData
                {
                    Text            = m.Desc,
                    WordWrap        = true,
                    BackgroundColor = m.Bg,
                    BorderBottom    = 0.25f,
                    BorderLeft      = 0.25f,
                    BorderRight     = 0.25f
                });
                td.Rows.Add(row);
            }

            return new TableTemplate
            {
                Name        = "Material Legend",
                Description = "Material legend with Rhino hatch patterns for concrete, masonry, and steel, plus solid colour fills for insulation and timber. Showcases hatch fills, merged title, and word wrap.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }

        private static TableTemplate CreateCoordinateTable()
        {
            // 5 columns: Point | X (m) | Y (m) | Z (m) | Description
            var td = new TableData
            {
                DefaultFontName = "Arial",
                DefaultFontSize = 3.0,
                ColumnWidths    = new List<double> { 15, 28, 28, 22, 57 },
                RowHeights      = Enumerable.Repeat(8.0, 10).ToList()
            };
            td.RowHeights[0] = 11.0;

            // Row 0: merged title spanning all 5 columns
            var titleRow = new TableRowData();
            titleRow.Cells.Add(new TableCellData
            {
                Text                = "SETTING-OUT COORDINATES",
                Bold                = true,
                FontSize            = 5.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Middle,
                BackgroundColor     = "#4A235A",
                TextColor           = "#FFFFFF",
                MergeRight          = 4,
                BorderTop           = 0.5f,
                BorderBottom        = 0.5f,
                BorderLeft          = 0.5f,
                BorderRight         = 0.5f
            });
            for (int c = 1; c < 5; c++)
                titleRow.Cells.Add(new TableCellData { IsMergedHidden = true });
            td.Rows.Add(titleRow);

            // Row 1: column headers
            string[] colHeaders = { "Point", "X (m)", "Y (m)", "Z (m)", "Description" };
            var hrow = new TableRowData { IsHeader = true };
            foreach (var h in colHeaders)
                hrow.Cells.Add(new TableCellData
                {
                    Text                = h,
                    Bold                = true,
                    FontSize            = 3.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    BackgroundColor     = "#7D3C98",
                    TextColor           = "#FFFFFF",
                    BorderTop           = 0.25f,
                    BorderBottom        = 0.5f,
                    BorderLeft          = 0.25f,
                    BorderRight         = 0.25f
                });
            td.Rows.Add(hrow);

            // 8 data rows with survey coordinates
            string[][] points =
            {
                new[] { "A1", "0.000",    "0.000",   "±0.000",  "Origin / benchmark"         },
                new[] { "A2", "12.500",   "0.000",   "±0.000",  "Column grid A-2"            },
                new[] { "A3", "25.000",   "0.000",   "±0.000",  "Column grid A-3"            },
                new[] { "B1", "0.000",    "8.400",   "±0.000",  "Column grid B-1"            },
                new[] { "B2", "12.500",   "8.400",   "±0.000",  "Column grid B-2 (centre)"   },
                new[] { "B3", "25.000",   "8.400",   "±0.000",  "Column grid B-3"            },
                new[] { "C1", "0.000",    "16.800",  "±0.000",  "Column grid C-1"            },
                new[] { "C2", "12.500",   "16.800",  "+3.600",  "Top of slab — level 1"      },
            };

            for (int r = 0; r < points.Length; r++)
            {
                var row = new TableRowData();
                string? altBg = r % 2 == 1 ? "#F4ECF7" : null;
                for (int c = 0; c < 5; c++)
                    row.Cells.Add(new TableCellData
                    {
                        Text                = points[r][c],
                        HorizontalAlignment = c < 4 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                        BackgroundColor     = altBg,
                        BorderBottom        = 0.25f,
                        BorderLeft          = 0.25f,
                        BorderRight         = 0.25f
                    });
                td.Rows.Add(row);
            }

            return new TableTemplate
            {
                Name        = "Setting-Out Coordinates",
                Description = "Survey / setting-out coordinate table with X, Y, Z values and point descriptions. Showcases merged title, centred numeric data, and alternating row colours.",
                IsBuiltIn   = true,
                TableData   = td
            };
        }
    }
}
