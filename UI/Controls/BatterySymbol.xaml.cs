using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulse.UI.Controls
{
    public partial class BatterySymbol : UserControl
    {
        // ── Dependency properties ─────────────────────────────────────────────

        public static readonly DependencyProperty StrokeBrushProperty =
            DependencyProperty.Register(
                nameof(StrokeBrush), typeof(Brush), typeof(BatterySymbol),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))));

        public static readonly DependencyProperty DimBrushProperty =
            DependencyProperty.Register(
                nameof(DimBrush), typeof(Brush), typeof(BatterySymbol),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF))));

        public Brush StrokeBrush
        {
            get => (Brush)GetValue(StrokeBrushProperty);
            set => SetValue(StrokeBrushProperty, value);
        }

        public Brush DimBrush
        {
            get => (Brush)GetValue(DimBrushProperty);
            set => SetValue(DimBrushProperty, value);
        }

        // ── Constructor ───────────────────────────────────────────────────────

        public BatterySymbol()
        {
            InitializeComponent();
            // DataContext = this so that {Binding StrokeBrush} / {Binding DimBrush}
            // in the XAML resolve against the DependencyProperties above.
            DataContext = this;
        }
    }
}
