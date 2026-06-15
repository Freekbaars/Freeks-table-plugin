using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CoreVAlign = RhinoTable.Core.Models.VerticalAlignment;

namespace RhinoTable.UI.Converters
{
    public class VAlignToVerticalAlignConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => value switch
        {
            CoreVAlign.Top    => VerticalAlignment.Top,
            CoreVAlign.Bottom => VerticalAlignment.Bottom,
            _                 => VerticalAlignment.Center
        };

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
