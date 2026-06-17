using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoTable.Core.Models;
using System.Drawing;

namespace RhinoTable.Core.Layout
{
    public class RhinoTableDrawer
    {
        // ── Nieuw blok aanmaken ───────────────────────────────────────────────
        public Guid Draw(RhinoDoc doc, TableData table, Point3d origin)
        {
            var (geoms, attrs) = BuildGeometry(table, doc);

            string json    = table.Serialize();
            string defName = !string.IsNullOrWhiteSpace(table.TableName)
                ? table.TableName
                : $"RhinoTable_{DateTime.Now:yyyyMMddHHmmss}";

            int defIndex = doc.InstanceDefinitions.Add(
                defName, json, Point3d.Origin, geoms, attrs);

            var xform = Transform.Translation(new Vector3d(origin.X, origin.Y, origin.Z));
            var objId = doc.Objects.AddInstanceObject(defIndex, xform);

            doc.Views.Redraw();
            return objId;
        }

        // ── Bestaand blok bijwerken (gekoppeld Excel) ─────────────────────────
        // Werkt de geometrie en JSON-beschrijving van de InstanceDefinition bij
        // zonder de positie of naam te wijzigen. Geeft true terug bij succes.
        public bool UpdateInPlace(RhinoDoc doc, TableData table, Guid instanceObjectId)
        {
            if (doc.Objects.FindId(instanceObjectId) is not InstanceObject obj)
                return false;

            var idef  = obj.InstanceDefinition;
            var (geoms, attrs) = BuildGeometry(table, doc);

            doc.InstanceDefinitions.ModifyGeometry(idef.Index, geoms, attrs);
            doc.InstanceDefinitions.Modify(idef, idef.Name, table.Serialize(), true);
            doc.Views.Redraw();
            return true;
        }

        // ── Geometrie bouwen (herbruikbaar door Draw én UpdateInPlace) ────────
        public (List<GeometryBase> Geoms, List<ObjectAttributes> Attrs) BuildGeometry(TableData table, RhinoDoc? doc = null)
        {
            var geometryList   = new List<GeometryBase>();
            var attributesList = new List<ObjectAttributes>();

            var xOffsets = new double[table.ColumnWidths.Count + 1];
            for (int c = 0; c < table.ColumnWidths.Count; c++)
                xOffsets[c + 1] = xOffsets[c] + table.ColumnWidths[c];

            var yOffsets = new double[table.RowHeights.Count + 1];
            for (int r = 0; r < table.RowHeights.Count; r++)
                yOffsets[r + 1] = yOffsets[r] + table.RowHeights[r];

            // Standaard rand-kleur: donkergrijs
            var defaultBorderColor = Color.FromArgb(90, 90, 90);

            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.IsMergedHidden) continue;

                    int endCol = Math.Min(c + cell.MergeRight + 1, table.ColumnWidths.Count);
                    int endRow = Math.Min(r + cell.MergeDown  + 1, table.RowHeights.Count);

                    double x0 = xOffsets[c];
                    double x1 = xOffsets[endCol];
                    double y0 = -yOffsets[r];
                    double y1 = -yOffsets[endRow];

                    // Vulkleur / arceerpatroon (voor randen zodat randen er bovenop liggen)
                    var bgColor    = ParseHexColor(cell.BackgroundColor);
                    var hatchColor = ParseHexColor(cell.HatchColor)
                                  ?? bgColor
                                  ?? Color.FromArgb(0x44, 0x72, 0xC4);

                    if (!string.IsNullOrEmpty(cell.HatchPatternName))
                    {
                        // Nieuw: Rhino arceerpatroon bovenop eventuele achtergrondkleur
                        DrawRhinoHatch(geometryList, attributesList,
                            x0, x1, y0, y1, cell.HatchPatternName, hatchColor, bgColor, doc,
                            cell.HatchScale, cell.HatchRotation);
                    }
                    else if (cell.FillPattern > 0)
                    {
                        // Legacy: handmatig getekende patronen (0-5)
                        DrawCellFill(geometryList, attributesList,
                            x0, x1, y0, y1, cell.FillPattern, hatchColor, bgColor, doc);
                    }

                    bool hasCustomBorder = cell.BorderTop > 0 || cell.BorderBottom > 0
                                       || cell.BorderLeft > 0 || cell.BorderRight > 0;

                    if (hasCustomBorder)
                    {
                        // Teken de vier zijden afzonderlijk met eigen dikte/kleur
                        var customColor = ParseHexColor(cell.BorderColor) ?? Color.Black;
                        AddBorderSide(geometryList, attributesList,
                            new Point3d(x0, y0, 0), new Point3d(x1, y0, 0),
                            cell.BorderTop,    customColor, defaultBorderColor);
                        AddBorderSide(geometryList, attributesList,
                            new Point3d(x0, y1, 0), new Point3d(x1, y1, 0),
                            cell.BorderBottom, customColor, defaultBorderColor);
                        AddBorderSide(geometryList, attributesList,
                            new Point3d(x0, y0, 0), new Point3d(x0, y1, 0),
                            cell.BorderLeft,   customColor, defaultBorderColor);
                        AddBorderSide(geometryList, attributesList,
                            new Point3d(x1, y0, 0), new Point3d(x1, y1, 0),
                            cell.BorderRight,  customColor, defaultBorderColor);
                    }
                    else
                    {
                        // Standaard: één gesloten rechthoek
                        var borderAttrs = new ObjectAttributes
                        {
                            ColorSource = ObjectColorSource.ColorFromObject,
                            ObjectColor = defaultBorderColor
                        };
                        geometryList.Add(new PolylineCurve(new[]
                        {
                            new Point3d(x0, y0, 0), new Point3d(x1, y0, 0),
                            new Point3d(x1, y1, 0), new Point3d(x0, y1, 0),
                            new Point3d(x0, y0, 0)
                        }));
                        attributesList.Add(borderAttrs);
                    }

                    // Celtekst
                    if (!string.IsNullOrWhiteSpace(cell.Text))
                    {
                        string fontName = cell.FontName ?? table.DefaultFontName;
                        double fontSize  = cell.FontSize  ?? table.DefaultFontSize;
                        const double margin = 0.5;

                        // Verticale positie & justificatie
                        double ty;
                        switch (cell.VerticalAlignment)
                        {
                            case VerticalAlignment.Top:
                                ty = y0 - margin;
                                break;
                            case VerticalAlignment.Bottom:
                                ty = y1 + margin;
                                break;
                            default: // Middle
                                ty = (y0 + y1) / 2.0;
                                break;
                        }

                        double tx;
                        TextJustification just = GetJustification(cell.HorizontalAlignment, cell.VerticalAlignment);
                        switch (cell.HorizontalAlignment)
                        {
                            case HorizontalAlignment.Left:    tx = x0 + margin; break;
                            case HorizontalAlignment.Right:   tx = x1 - margin; break;
                            default:                          tx = (x0 + x1) / 2.0; break;
                        }

                        var te = new TextEntity
                        {
                            Plane         = new Plane(new Point3d(tx, ty, 0), Vector3d.ZAxis),
                            TextHeight    = fontSize,
                            Justification = just,
                        };

                        // Terugloop: handmatig regelafbrekingen invoegen op basis van geschatte tekenbreedte
                        string displayText = (cell.WordWrap && (x1 - x0) > 2 * margin)
                            ? WrapText(cell.Text, fontSize, x1 - x0 - 2 * margin)
                            : cell.Text;

                        bool hasMarkup = displayText.Contains("^{") || displayText.Contains("_{");
                        bool needsRtf  = hasMarkup || cell.Bold || cell.Italic;
                        if (needsRtf)
                            te.RichText  = DrawHelper.BuildRichText(displayText, fontName, fontSize, cell.Bold, cell.Italic);
                        else
                            te.PlainText = displayText;

                        var textAttrs = new ObjectAttributes();
                        var textColor = ParseHexColor(cell.TextColor);
                        if (textColor.HasValue)
                        {
                            textAttrs.ColorSource = ObjectColorSource.ColorFromObject;
                            textAttrs.ObjectColor = textColor.Value;
                        }

                        geometryList.Add(te);
                        attributesList.Add(textAttrs);
                    }
                }
            }

            return (geometryList, attributesList);
        }

        // Tekent eerst een effen achtergrond (indien bgColor), dan een Rhino arceerpatroon erop.
        private static void DrawRhinoHatch(
            List<GeometryBase> geoms, List<ObjectAttributes> attrs,
            double x0, double x1, double y0, double y1,
            string patternName, Color hatchColor, Color? bgColor, RhinoDoc? doc,
            double scale = 1.0, double rotationDegrees = 0.0)
        {
            if (doc == null) return;

            ObjectAttributes Attr(Color c) => new ObjectAttributes
            {
                ColorSource = ObjectColorSource.ColorFromObject,
                ObjectColor = c
            };

            // CCW boundary zodat Rhino de binnenkant als vulling herkent
            PolylineCurve MakeBoundary() => new PolylineCurve(new[]
            {
                new Point3d(x0, y1, 0), new Point3d(x1, y1, 0),
                new Point3d(x1, y0, 0), new Point3d(x0, y0, 0),
                new Point3d(x0, y1, 0)
            });

            // Index 0 = Solid; FindName is fallback voor niet-Engelstalige Rhino
            int solidIdx = doc.HatchPatterns.FindName("Solid")?.Index ?? 0;
            double tol = doc.ModelAbsoluteTolerance;

            // Stap 1: effen achtergrond
            if (bgColor.HasValue)
            {
                var bg = Hatch.Create(new Curve[] { MakeBoundary() }, solidIdx, 0, 1, tol);
                if (bg != null && bg.Length > 0)
                    foreach (var h in bg) { geoms.Add(h); attrs.Add(Attr(bgColor.Value)); }
            }

            // Stap 2: Rhino arceerpatroon erop
            var rhinoPattern = doc.HatchPatterns.FindName(patternName);
            if (rhinoPattern != null)
            {
                double rotRad = rotationDegrees * Math.PI / 180.0;
                var hs = Hatch.Create(new Curve[] { MakeBoundary() }, rhinoPattern.Index, rotRad, scale, tol);
                if (hs != null && hs.Length > 0)
                    foreach (var h in hs) { geoms.Add(h); attrs.Add(Attr(hatchColor)); }
            }
        }

        // Voegt Rhino-standaardpatronen toe aan het document als ze er nog niet in zitten.
        // HatchPattern.Defaults levert kant-en-klare patronen met FillType=Lines en correcte HatchLines.
        // Dit vermijdt het handmatig aanmaken van HatchLine-objecten waarbij FillType standaard Solid blijft.
        public static void EnsureBuiltinHatchPatterns(RhinoDoc doc)
        {
            var toAdd = new (string Name, HatchPattern? Pattern)[]
            {
                ("Hatch1", HatchPattern.Defaults.Hatch1),
                ("Hatch2", HatchPattern.Defaults.Hatch2),
                ("Hatch3", HatchPattern.Defaults.Hatch3),
                ("Grid",   HatchPattern.Defaults.Grid),
                ("Plus",   HatchPattern.Defaults.Plus),
                ("Dash",   HatchPattern.Defaults.Dash),
            };

            foreach (var (name, pattern) in toAdd)
            {
                if (pattern == null) continue;
                if (doc.HatchPatterns.FindName(name) != null) continue;
                pattern.Name = name;
                doc.HatchPatterns.Add(pattern);
            }
        }

        private static void DrawCellFill(
            List<GeometryBase> geoms, List<ObjectAttributes> attrs,
            double x0, double x1, double y0, double y1,
            int pattern, Color hatchColor, Color? bgColor, RhinoDoc? doc)
        {
            ObjectAttributes Attr(Color c) => new ObjectAttributes
            {
                ColorSource = ObjectColorSource.ColorFromObject,
                ObjectColor = c
            };

            PolylineCurve MakeBoundary() => new PolylineCurve(new[]
            {
                new Point3d(x0, y0, 0), new Point3d(x1, y0, 0),
                new Point3d(x1, y1, 0), new Point3d(x0, y1, 0),
                new Point3d(x0, y0, 0)
            });

            int SolidIdx() => doc?.HatchPatterns.FindName("Solid")?.Index ?? 0;

            if (pattern == 1 && doc != null)
            {
                // Effen vulling: gebruik bgColor als beschikbaar, anders hatchColor
                var fillColor = bgColor ?? hatchColor;
                var hatches = Hatch.Create(new Curve[] { MakeBoundary() }, SolidIdx(), 0, 1, 0.001);
                if (hatches != null)
                    foreach (var h in hatches) { geoms.Add(h); attrs.Add(Attr(fillColor)); }
                return;
            }

            // Arceerpatroon: teken eerst achtergrondvlak als bgColor apart is ingesteld
            if (bgColor.HasValue && doc != null)
            {
                var hatches = Hatch.Create(new Curve[] { MakeBoundary() }, SolidIdx(), 0, 1, 0.001);
                if (hatches != null)
                    foreach (var h in hatches) { geoms.Add(h); attrs.Add(Attr(bgColor.Value)); }
            }

            const double spacing = 2.5; // mm tussen arceerlijnen

            switch (pattern)
            {
                case 2: // Horizontaal
                    for (double y = y0 - spacing; y > y1; y -= spacing)
                    {
                        geoms.Add(new LineCurve(new Point3d(x0, y, 0), new Point3d(x1, y, 0)));
                        attrs.Add(Attr(hatchColor));
                    }
                    break;

                case 3: // Verticaal
                    for (double x = x0 + spacing; x < x1; x += spacing)
                    {
                        geoms.Add(new LineCurve(new Point3d(x, y0, 0), new Point3d(x, y1, 0)));
                        attrs.Add(Attr(hatchColor));
                    }
                    break;

                case 4: // Diagonaal (45°, links-boven → rechts-onder)
                    for (double sx = x0; sx < x1; sx += spacing)
                        AddDiagonalLine(geoms, attrs, sx, y0, x0, x1, y0, y1, Attr(hatchColor));
                    for (double sy = y0 - spacing; sy > y1; sy -= spacing)
                        AddDiagonalLine(geoms, attrs, x0, sy, x0, x1, y0, y1, Attr(hatchColor));
                    break;

                case 5: // Kruis
                    for (double y = y0 - spacing; y > y1; y -= spacing)
                    {
                        geoms.Add(new LineCurve(new Point3d(x0, y, 0), new Point3d(x1, y, 0)));
                        attrs.Add(Attr(hatchColor));
                    }
                    for (double x = x0 + spacing; x < x1; x += spacing)
                    {
                        geoms.Add(new LineCurve(new Point3d(x, y0, 0), new Point3d(x, y1, 0)));
                        attrs.Add(Attr(hatchColor));
                    }
                    break;
            }
        }

        private static void AddDiagonalLine(
            List<GeometryBase> geoms, List<ObjectAttributes> attrs,
            double sx, double sy,
            double x0, double x1, double y0, double y1,
            ObjectAttributes a)
        {
            // Richting (1, -1): naar rechts en naar beneden in Rhino (y omhoog)
            double tRight  = x1 - sx;
            double tBottom = sy - y1;
            double t = Math.Min(tRight, tBottom);
            if (t < 0.001) return;
            geoms.Add(new LineCurve(new Point3d(sx, sy, 0), new Point3d(sx + t, sy - t, 0)));
            attrs.Add(a);
        }

        private static void AddBorderSide(
            List<GeometryBase> geoms, List<ObjectAttributes> attrs,
            Point3d p0, Point3d p1, float thickness, Color customColor, Color defaultColor)
        {
            var line = new LineCurve(p0, p1);
            var a = new ObjectAttributes { ColorSource = ObjectColorSource.ColorFromObject };
            if (thickness > 0)
            {
                a.ObjectColor = customColor;
                a.PlotWeight  = thickness;
            }
            else
            {
                a.ObjectColor = defaultColor;
                a.PlotWeight  = 0.25;
            }
            geoms.Add(line);
            attrs.Add(a);
        }

        // Schat regelafbrekingen voor Rhino-uitvoer (geen native wrap-API beschikbaar).
        // Gemiddelde tekenbreedte ≈ fontSize × 0.55 mm voor een standaard schreefloos font.
        private static string WrapText(string text, double fontSize, double widthMm)
        {
            int charsPerLine = Math.Max(1, (int)(widthMm / (fontSize * 0.60)));
            var words  = text.Split(' ');
            var sb     = new System.Text.StringBuilder();
            int lineLen = 0;
            foreach (var word in words)
            {
                if (lineLen > 0 && lineLen + 1 + word.Length > charsPerLine)
                { sb.Append('\n'); lineLen = 0; }
                else if (lineLen > 0)
                { sb.Append(' '); lineLen++; }
                sb.Append(word);
                lineLen += word.Length;
            }
            return sb.ToString();
        }

        private static TextJustification GetJustification(HorizontalAlignment h, VerticalAlignment v)
        {
            return (h, v) switch
            {
                (HorizontalAlignment.Left,   VerticalAlignment.Top)    => TextJustification.TopLeft,
                (HorizontalAlignment.Center, VerticalAlignment.Top)    => TextJustification.TopCenter,
                (HorizontalAlignment.Right,  VerticalAlignment.Top)    => TextJustification.TopRight,
                (HorizontalAlignment.Left,   VerticalAlignment.Middle) => TextJustification.MiddleLeft,
                (HorizontalAlignment.Center, VerticalAlignment.Middle) => TextJustification.MiddleCenter,
                (HorizontalAlignment.Right,  VerticalAlignment.Middle) => TextJustification.MiddleRight,
                (HorizontalAlignment.Left,   VerticalAlignment.Bottom) => TextJustification.BottomLeft,
                (HorizontalAlignment.Center, VerticalAlignment.Bottom) => TextJustification.BottomCenter,
                (HorizontalAlignment.Right,  VerticalAlignment.Bottom) => TextJustification.BottomRight,
                _ => TextJustification.MiddleLeft
            };
        }

        private static Color? ParseHexColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return null;
            try
            {
                byte r = Convert.ToByte(hex[0..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                return Color.FromArgb(r, g, b);
            }
            catch { return null; }
        }
    }
}
