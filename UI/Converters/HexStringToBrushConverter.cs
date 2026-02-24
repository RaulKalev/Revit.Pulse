using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse.UI.Converters
{
    /// <summary>
    /// Converts a "#RRGGBB" or "#AARRGGBB" hex string to a <see cref="SolidColorBrush"/>.
    /// Returns a transparent brush when the string is null or invalid.
    /// </summary>
    [ValueConversion(typeof(string), typeof(Brush))]
    public sealed class HexStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(color);
                }
                catch { }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
