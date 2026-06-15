using System;
using System.Globalization;
using System.Windows.Data;

namespace RhinoTable.UI.Converters
{
    public class MmToPixelConverter : IValueConverter
    {
        const double PxPerMm = 3.7795275591; // at 96 DPI

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double mm ? mm * PxPerMm : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double px ? px / PxPerMm : 0.0;
    }
}
