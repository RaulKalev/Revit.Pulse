using System;
using System.Windows;
using System.Windows.Media;

namespace Pulse.UI.Controls
{
    public partial class GaugeControl : System.Windows.Controls.UserControl
    {
        // Arc center and radius (to stroke centerline). StrokeThickness=12 in XAML.
        private const double Cx = 70;
        private const double Cy = 82;
        private const double R  = 52;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
                new PropertyMetadata(0.0, OnGaugeDataChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GaugeControl),
                new PropertyMetadata(100.0, OnGaugeDataChanged));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(GaugeControl),
                new PropertyMetadata(string.Empty, OnGaugeDataChanged));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(GaugeControl),
                new PropertyMetadata(string.Empty, OnGaugeDataChanged));

        public double Value   { get => (double)GetValue(ValueProperty);  set => SetValue(ValueProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public string Label   { get => (string)GetValue(LabelProperty);  set => SetValue(LabelProperty, value); }
        public string Unit    { get => (string)GetValue(UnitProperty);   set => SetValue(UnitProperty, value); }

        public GaugeControl()
        {
            InitializeComponent();
            Loaded += (_, __) => Refresh();
        }

        private static void OnGaugeDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((GaugeControl)d).Refresh();

        private void Refresh()
        {
            double ratio = Maximum > 0 ? Math.Min(Value / Maximum, 1.0) : 0;

            DrawBackgroundArc();
            DrawValueArc(ratio);
            UpdateColor(ratio);
            UpdateTexts(ratio);
        }

        // Draw the full 180-degree background arc (open stroked path, no fill).
        // Uses 0.9999*PI to avoid the degenerate case where start==end for exactly 180 degrees.
        private void DrawBackgroundArc()
        {
            double theta = 0.9999 * Math.PI;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                double sx = Cx - R;
                double sy = Cy;
                double ex = Cx - R * Math.Cos(theta);
                double ey = Cy - R * Math.Sin(theta);   // sin is positive => Y goes up

                ctx.BeginFigure(new Point(sx, sy), isFilled: false, isClosed: false);
                ctx.ArcTo(new Point(ex, ey),
                    new Size(R, R), 0,
                    isLargeArc: true, sweepDirection: SweepDirection.Clockwise,
                    isStroked: true, isSmoothJoin: true);
            }
            geo.Freeze();
            BackTrackPath.Data = geo;
        }

        // Draw the value arc from the left endpoint sweeping clockwise (over the top) by ratio*PI.
        private void DrawValueArc(double ratio)
        {
            if (ratio <= 0.005)
            {
                ValueArcPath.Data = Geometry.Empty;
                return;
            }

            double theta = ratio * Math.PI;

            double sx = Cx - R;
            double sy = Cy;
            double ex = Cx - R * Math.Cos(theta);
            double ey = Cy - R * Math.Sin(theta);   // negative screen-Y = up

            // We always sweep <= 180 degrees so largeArc is always false
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(sx, sy), isFilled: false, isClosed: false);
                ctx.ArcTo(new Point(ex, ey),
                    new Size(R, R), 0,
                    false, SweepDirection.Clockwise,
                    isStroked: true, isSmoothJoin: true);
            }
            geo.Freeze();
            ValueArcPath.Data = geo;
        }

        private void UpdateColor(double ratio)
        {
            Color c;
            if (ratio < 0.70)      c = Color.FromRgb(0x4C, 0xAF, 0x50); // green
            else if (ratio < 0.90) c = Color.FromRgb(0xFF, 0xC1, 0x07); // amber
            else                   c = Color.FromRgb(0xF4, 0x43, 0x36); // red
            ValueArcPath.Stroke = new SolidColorBrush(c);
        }

        private void UpdateTexts(double ratio)
        {
            ValueText.Text   = FormatNumber(Value) + " / " + FormatNumber(Maximum) + " " + (Unit ?? string.Empty);
            int pct          = Maximum > 0 ? (int)Math.Round(Value / Maximum * 100) : 0;
            PercentText.Text = pct + "%";
            LabelText.Text   = Label ?? string.Empty;
        }

        private static string FormatNumber(double v)
            => (v % 1.0 == 0.0) ? ((long)v).ToString() : v.ToString("0.#");
    }
}
