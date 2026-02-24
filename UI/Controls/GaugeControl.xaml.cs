using System;
using System.Windows;
using System.Windows.Media;

namespace Pulse.UI.Controls
{
    /// <summary>
    /// Speedometer-style semicircular gauge.
    /// Shows a value/maximum ratio as a coloured arc:
    ///   green (< 70 %)  ->  yellow (< 90 %)  ->  red (>= 90 %)
    /// Layout: label at top, thin arc ring, combined "V / M unit" text, percentage below.
    /// </summary>
    public partial class GaugeControl : System.Windows.Controls.UserControl
    {
        // -- Geometry constants -----------------------------------------------
        private const double Cx     = 70;
        private const double Cy     = 78;
        private const double OuterR = 58;
        private const double InnerR = 35;

        // -- Dependency Properties --------------------------------------------

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

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        // -- Constructor ------------------------------------------------------

        public GaugeControl()
        {
            InitializeComponent();
            Loaded += (_, __) => Refresh();
        }

        // -- Change callback --------------------------------------------------

        private static void OnGaugeDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((GaugeControl)d).Refresh();

        // -- Core refresh -----------------------------------------------------

        private void Refresh()
        {
            double ratio = Maximum > 0 ? Math.Min(Value / Maximum, 1.2) : 0;
            double clampedRatio = Math.Min(ratio, 1.0);

            UpdateArc(clampedRatio);
            UpdateColor(ratio);
            UpdateTexts(ratio);
        }

        private void UpdateArc(double ratio)
        {
            if (ratio <= 0.005)
            {
                ValueArcPath.Data = Geometry.Empty;
                return;
            }

            double theta = ratio * Math.PI;

            double ox0 = Cx - OuterR;
            double oy0 = Cy;
            double ox1 = Cx - OuterR * Math.Cos(theta);
            double oy1 = Cy - OuterR * Math.Sin(theta);

            double ix0 = Cx - InnerR;
            double iy0 = Cy;
            double ix1 = Cx - InnerR * Math.Cos(theta);
            double iy1 = Cy - InnerR * Math.Sin(theta);

            bool largeArc = ratio > 0.5;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(ox0, oy0), isFilled: true, isClosed: true);
                ctx.ArcTo(new Point(ox1, oy1),
                    new Size(OuterR, OuterR), 0,
                    largeArc, SweepDirection.Counterclockwise,
                    isStroked: false, isSmoothJoin: true);

                ctx.LineTo(new Point(ix1, iy1), isStroked: false, isSmoothJoin: true);
                ctx.ArcTo(new Point(ix0, iy0),
                    new Size(InnerR, InnerR), 0,
                    largeArc, SweepDirection.Clockwise,
                    isStroked: false, isSmoothJoin: true);
            }
            geo.Freeze();
            ValueArcPath.Data = geo;
        }

        private void UpdateColor(double ratio)
        {
            Color fill;
            if (ratio < 0.70)
                fill = Color.FromRgb(0x4C, 0xAF, 0x50); // green
            else if (ratio < 0.90)
                fill = Color.FromRgb(0xFF, 0xC1, 0x07); // amber
            else
                fill = Color.FromRgb(0xF4, 0x43, 0x36); // red

            ValueArcPath.Fill = new SolidColorBrush(fill);
        }

        private void UpdateTexts(double ratio)
        {
            string unit  = Unit  ?? string.Empty;
            string label = Label ?? string.Empty;

            ValueText.Text   = FormatNumber(Value) + " / " + FormatNumber(Maximum) + " " + unit;
            int pct          = Maximum > 0 ? (int)Math.Round(Value / Maximum * 100) : 0;
            PercentText.Text = pct + "%";
            LabelText.Text   = label;
        }

        private static string FormatNumber(double v)
            => (v % 1.0 == 0.0) ? ((long)v).ToString() : v.ToString("0.#");
    }
}
