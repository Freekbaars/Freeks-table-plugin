using RhinoTable.Core.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RhinoTable.UI.Converters
{
    /// <summary>IsMergedHidden bool → achtergrondkleur cel</summary>
    public class MergedHiddenToBrushConverter : IValueConverter
    {
        private static readonly Brush _grey = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE2));

        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? _grey : Brushes.Transparent;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>bool → Visibility (true = Collapsed, false = Visible)</summary>
    public class BoolToInverseVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>int → Visibility (> 0 = Visible, 0 = Collapsed)</summary>
    public class IntPositiveToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>Hex-kleurstring → SolidColorBrush. Null/leeg → Brushes.Black.</summary>
    public class ColorStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                var col = ParseHex(s);
                if (col.HasValue) return new SolidColorBrush(col.Value);
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;

        internal static Color? ParseHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return null;
            try
            {
                byte r = System.Convert.ToByte(hex[0..2], 16);
                byte g = System.Convert.ToByte(hex[2..4], 16);
                byte b = System.Convert.ToByte(hex[4..6], 16);
                return Color.FromRgb(r, g, b);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// MultiBinding: [0]=BorderLeft, [1]=BorderTop, [2]=BorderRight, [3]=BorderBottom (float mm)
    /// → WPF Thickness (pixels at 96 DPI).
    /// </summary>
    public class CellBorderThicknessConverter : IMultiValueConverter
    {
        private const double PxPerMm = 3.7795275591;

        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            double l   = values.Length > 0 && values[0] is float fl ? fl * PxPerMm : 0;
            double top = values.Length > 1 && values[1] is float ft ? ft * PxPerMm : 0;
            double r   = values.Length > 2 && values[2] is float fr ? fr * PxPerMm : 0;
            double b   = values.Length > 3 && values[3] is float fb ? fb * PxPerMm : 0;
            return new Thickness(l, top, r, b);
        }

        public object[] ConvertBack(object value, Type[] types, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>Hex-kleurstring → SolidColorBrush voor celranden. Null/leeg → Brushes.Black.</summary>
    public class CellBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                var col = ColorStringToBrushConverter.ParseHex(s);
                if (col.HasValue) return new SolidColorBrush(col.Value);
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>Arceerpatroonnaam (string) → VisualBrush preview voor de dropdown.</summary>
    public class HatchNameToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            string? name = value as string;
            if (string.IsNullOrEmpty(name) || name == "(geen arcering)")
                return Brushes.Transparent;
            return FillPatternBrushConverter.BuildBrush(1, "#FFFFFF", "#404040", name);
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// Bouwt een vulpatroon-brush op basis van patroon-index en kleur.
    /// Pattern 0=geen, 1=effen, 2=horizontaal, 3=verticaal, 4=diagonaal, 5=kruis.
    /// Gebruikt VisualBrush i.p.v. DrawingBrush — betrouwbaarder in DataGrid cell templates.
    /// </summary>
    public class FillPatternBrushConverter : IMultiValueConverter
    {
        private static readonly Color _defaultFill = Color.FromRgb(0x44, 0x72, 0xC4);

        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            int pattern = values.Length > 0 && values[0] is int i ? i : 0;
            string? hex = values.Length > 1 ? values[1] as string : null;
            return BuildBrush(pattern, hex);
        }

        public object[] ConvertBack(object value, Type[] types, object p, CultureInfo c)
            => throw new NotImplementedException();

        // bgColorHex       = achtergrondkleur (solid fill / achter arceerlijnen)
        // hatchColorHex    = kleur van de arceerlijnen (null = gebruik bgColorHex)
        // hatchPatternName = Rhino-patroonnaam; als ingesteld: toon bg + diagonale preview-lijnen
        public static Brush BuildBrush(int pattern, string? bgColorHex, string? hatchColorHex = null, string? hatchPatternName = null)
        {
            Color? bgColor = ColorStringToBrushConverter.ParseHex(bgColorHex);

            if (!string.IsNullOrEmpty(hatchPatternName))
            {
                // Rhino arceerpatroon: toon achtergrond + diagonale lijnen als visuele indicator.
                // De exacte penseelvorm is niet beschikbaar in WPF; de Rhino-uitvoer gebruikt het echte patroon.
                Color lineColor = ColorStringToBrushConverter.ParseHex(hatchColorHex) ?? bgColor ?? _defaultFill;
                return MakeHatchBrush(lineColor, 4, bgColor);
            }

            if (pattern == 0) return Brushes.Transparent;
            if (pattern == 1) return bgColor.HasValue ? new SolidColorBrush(bgColor.Value) : Brushes.Transparent;

            Color lineCol = ColorStringToBrushConverter.ParseHex(hatchColorHex) ?? bgColor ?? _defaultFill;
            Color? bg = (hatchColorHex != null && bgColor.HasValue) ? bgColor : (Color?)null;
            return MakeHatchBrush(lineCol, pattern, bg);
        }

        // Gebruikt VisualBrush met Canvas + Line elementen — werkt betrouwbaar in alle WPF contexts.
        private static VisualBrush MakeHatchBrush(Color lineColor, int pattern, Color? bgColor = null)
        {
            const double size = 8.0;
            var stroke = new SolidColorBrush(lineColor);
            var canvas = new Canvas
            {
                Width      = size,
                Height     = size,
                Background = bgColor.HasValue ? new SolidColorBrush(bgColor.Value) : Brushes.Transparent
            };

            switch (pattern)
            {
                case 2: // Horizontaal
                    canvas.Children.Add(Line(0, 4, 8, 4, stroke));
                    break;
                case 3: // Verticaal
                    canvas.Children.Add(Line(4, 0, 4, 8, stroke));
                    break;
                case 4: // Diagonaal — twee lijnen zodat de randen van de tegel aansluiten
                    canvas.Children.Add(Line(-1, -1, 9, 9, stroke));
                    canvas.Children.Add(Line(7, -1, 17, 9, stroke));
                    break;
                case 5: // Kruis
                    canvas.Children.Add(Line(0, 4, 8, 4, stroke));
                    canvas.Children.Add(Line(4, 0, 4, 8, stroke));
                    break;
            }

            return new VisualBrush(canvas)
            {
                TileMode      = TileMode.Tile,
                Stretch       = Stretch.None,
                Viewbox       = new Rect(0, 0, size, size),
                ViewboxUnits  = BrushMappingMode.Absolute,
                Viewport      = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute,
            };
        }

        private static Line Line(double x1, double y1, double x2, double y2, Brush stroke)
            => new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = 1.0 };
    }
}
