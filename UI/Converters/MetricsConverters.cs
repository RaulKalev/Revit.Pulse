using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Pulse.Core.Modules.Metrics;

namespace Pulse.UI.Converters
{
    /// <summary>
    /// Converts <see cref="CapacityStatus"/> to a <see cref="SolidColorBrush"/>
    /// for capacity gauge threshold colour indicators.
    /// </summary>
    public class CapacityStatusToBrushConverter : IValueConverter
    {
        // Normal  → semi-transparent white (neutral)
        // Warning → amber
        // Critical → red
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CapacityStatus status)
            {
                switch (status)
                {
                    case CapacityStatus.Critical:
                        return new SolidColorBrush(Color.FromRgb(220, 107, 107)); // #DC6B6B
                    case CapacityStatus.Warning:
                        return new SolidColorBrush(Color.FromRgb(255, 193, 7));   // #FFC107
                    default:
                        return new SolidColorBrush(Color.FromArgb(100, 238, 238, 238)); // dim white
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts <see cref="HealthStatus"/> to a <see cref="SolidColorBrush"/>
    /// for health-row status dots and section headers.
    /// </summary>
    public class HealthStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HealthStatus status)
            {
                switch (status)
                {
                    case HealthStatus.Error:
                        return new SolidColorBrush(Color.FromRgb(220, 107, 107)); // #DC6B6B
                    case HealthStatus.Warning:
                        return new SolidColorBrush(Color.FromRgb(255, 193, 7));   // #FFC107
                    default:
                        return new SolidColorBrush(Color.FromRgb(76,  175,  80)); // #4CAF50
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns a MaterialDesign icon kind string for a <see cref="HealthStatus"/>.
    /// Used as a binding parameter for PackIcon.Kind.
    /// </summary>
    public class HealthStatusToIconKindConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HealthStatus status)
            {
                switch (status)
                {
                    case HealthStatus.Error:   return "AlertCircle";
                    case HealthStatus.Warning: return "Alert";
                    default:                   return "CheckCircle";
                }
            }
            return "HelpCircle";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
