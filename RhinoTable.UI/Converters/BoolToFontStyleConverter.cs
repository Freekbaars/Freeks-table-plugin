using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RhinoTable.UI.Converters
{
    public class BoolToFontStyleConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? FontStyles.Italic : FontStyles.Normal;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is FontStyle fs && fs == FontStyles.Italic;
    }
}
