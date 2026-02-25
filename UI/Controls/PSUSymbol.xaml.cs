using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulse.UI.Controls
{
    public partial class PSUSymbol : UserControl
    {
        // ── Dependency properties ─────────────────────────────────────────────

        public static readonly DependencyProperty StrokeBrushProperty =
            DependencyProperty.Register(
                nameof(StrokeBrush), typeof(Brush), typeof(PSUSymbol),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0))));

        public static readonly DependencyProperty DimBrushProperty =
            DependencyProperty.Register(
                nameof(DimBrush), typeof(Brush), typeof(PSUSymbol),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0))));

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

        public PSUSymbol()
        {
            InitializeComponent();
            // DataContext = this so the XAML {Binding ...} expressions resolve
            // against the DependencyProperties defined above.
            DataContext = this;
        }
    }
}
