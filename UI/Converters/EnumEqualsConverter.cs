using System;
using System.Globalization;
using System.Windows.Data;

namespace Pulse.UI.Converters
{
    /// <summary>
    /// Returns <c>true</c> when the bound enum value equals the converter parameter.
    /// Used to highlight the active tool button in the symbol designer.
    /// </summary>
    [ValueConversion(typeof(Enum), typeof(bool))]
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter != null && value.Equals(parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
