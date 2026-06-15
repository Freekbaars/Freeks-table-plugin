using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RhinoTable.UI.Converters
{
    public class BoolToTextWrappingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TextWrapping.Wrap : TextWrapping.NoWrap;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TextWrapping tw && tw == TextWrapping.Wrap;
    }
}
