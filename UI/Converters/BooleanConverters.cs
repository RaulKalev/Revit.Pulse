using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse.UI.Converters
{
    /// <summary>Converts a hex colour string (e.g. "#FF0000") to a <see cref="SolidColorBrush"/>.
    /// Returns <see cref="DependencyProperty.UnsetValue"/> when the string is null/empty or unparseable.</summary>
    public class ColorHexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var hex = value as string;
            if (string.IsNullOrWhiteSpace(hex)) return DependencyProperty.UnsetValue;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { return DependencyProperty.UnsetValue; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Inverts a boolean value.</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }
    }

    /// <summary>Returns Visibility.Visible when value > 0, otherwise Collapsed. Pass ConverterParameter=Invert to reverse.</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
            bool hasItems = value is int count && count > 0;
            if (invert) hasItems = !hasItems;
            return hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Returns Visibility.Collapsed when value is true, Visible when false.</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Returns Visibility.Collapsed when value is null or empty string.</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Converts Severity enum to a SolidColorBrush for visual indicators.</summary>
    public class SeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Core.Rules.Severity severity)
            {
                switch (severity)
                {
                    case Core.Rules.Severity.Error:
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 107, 107));
                    case Core.Rules.Severity.Warning:
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
                    case Core.Rules.Severity.Info:
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 181, 246));
                    default:
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
                }
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
