using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RhinoTable.UI.Converters
{
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? FontWeights.Bold : FontWeights.Normal;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is FontWeight fw && fw == FontWeights.Bold;
    }
}
