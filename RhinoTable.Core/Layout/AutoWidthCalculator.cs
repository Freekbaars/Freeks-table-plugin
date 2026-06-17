using RhinoTable.Core.Models;

namespace RhinoTable.Core.Layout
{
    public class AutoWidthCalculator
    {
        // Marge links + rechts in een cel (zelfde als RhinoTableDrawer.margin * 2)
        private const double CellPaddingMm = 5.0;
        private const double MinWidthMm    = 10.0;

        public void Apply(TableData table)
        {
            double defaultSize = table.DefaultFontSize;

            for (int c = 0; c < table.ColumnWidths.Count; c++)
            {
                double maxWidth = MinWidthMm;

                foreach (var row in table.Rows)
                {
                    if (c >= row.Cells.Count) continue;
                    var cell = row.Cells[c];
                    if (cell.IsMergedHidden) continue;

                    double fontSize   = cell.FontSize ?? defaultSize;
                    // 0.60 * fontSize is iets ruimer dan de WrapText factor (0.55)
                    // zodat er altijd ademruimte overblijft na het wikkelen.
                    double baseChar   = fontSize * 1;
                    double boldFactor = cell.Bold ? 1.15 : 1.0;

                    foreach (var line in cell.Text.Split('\n'))
                    {
                        double lineW = MeasureLine(line, baseChar) * boldFactor + CellPaddingMm;
                        if (lineW > maxWidth) maxWidth = lineW;
                    }
                }

                table.ColumnWidths[c] = Math.Round(maxWidth, 1);
            }
        }

        // Schat de breedte van één regel tekst in mm.
        private static double MeasureLine(string line, double baseChar)
        {
            double w = 0;
            foreach (char ch in line)
                w += CharFactor(ch);
            return w * baseChar;
        }

        // Breedtefactor per tekenklasse (genormaliseerd op gemiddeld lowercase = 1.0).
        private static double CharFactor(char ch) => ch switch
        {
            'M' or 'W'                       => 1.40,
            'm' or 'w'                       => 1.30,
            'O' or 'Q' or 'C' or 'G' or 'D' => 1.15,
            _ when char.IsUpper(ch)          => 1.10,
            'i' or 'l' or '1' or '|'
                or '!' or '.' or ',' or ';'
                or ':' or '\'' or '"'        => 0.45,
            'f' or 'j' or 'r' or 't'        => 0.70,
            ' '                              => 0.50,
            _                                => 1.00,
        };
    }
}
