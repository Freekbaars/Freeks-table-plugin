using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CoreAlign = RhinoTable.Core.Models.HorizontalAlignment;

namespace RhinoTable.UI.Converters
{
    public class HAlignToTextAlignConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => value switch
        {
            CoreAlign.Center => TextAlignment.Center,
            CoreAlign.Right  => TextAlignment.Right,
            _                => TextAlignment.Left
        };

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
