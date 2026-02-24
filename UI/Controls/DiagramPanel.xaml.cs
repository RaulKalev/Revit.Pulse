using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialDesignThemes.Wpf;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Controls
{
    public partial class DiagramPanel : UserControl
    {
        private const double ExpandedWidth  = 300;
        private const double CollapsedWidth = 32;
        private const double LabelColumnWidth = 70;
        private const double MarginTop    = 16;
        private const double MarginBottom = 16;

        private bool _isExpanded = false; // starts collapsed

        public DiagramPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => ApplyState();
        }

        // ── Toggle ────────────────────────────────────────────────────────

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
            ApplyState();
        }

        private void ApplyState()
        {
            if (_isExpanded)
            {
                Width                       = ExpandedWidth;
                DiagramContent.Visibility   = Visibility.Visible;
                HeaderTitleStack.Visibility = Visibility.Visible;
                CollapsedLabel.Visibility   = Visibility.Collapsed;
                ToggleIcon.Kind             = PackIconKind.ChevronRight;
            }
            else
            {
                Width                       = CollapsedWidth;
                DiagramContent.Visibility   = Visibility.Collapsed;
                HeaderTitleStack.Visibility = Visibility.Collapsed;
                CollapsedLabel.Visibility   = Visibility.Visible;
                ToggleIcon.Kind             = PackIconKind.ChevronLeft;
            }
        }

        // ── DataContext / Levels wiring ───────────────────────────────────

        private DiagramViewModel _currentVm;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_currentVm != null)
                _currentVm.Levels.CollectionChanged -= OnLevelsChanged;

            _currentVm = DataContext as DiagramViewModel;

            if (_currentVm != null)
                _currentVm.Levels.CollectionChanged += OnLevelsChanged;

            DrawLevels();
        }

        private void OnLevelsChanged(object sender, NotifyCollectionChangedEventArgs e)
            => DrawLevels();

        // ── SizeChanged ───────────────────────────────────────────────────

        private void DiagramContent_SizeChanged(object sender, SizeChangedEventArgs e)
            => DrawLevels();

        // ── Drawing ───────────────────────────────────────────────────────

        private void DrawLevels()
        {
            DiagramCanvas.Children.Clear();

            if (_currentVm == null || _currentVm.Levels.Count == 0) return;

            double w = DiagramContent.ActualWidth;
            double h = DiagramContent.ActualHeight;
            if (w < 1 || h < 1) return;

            DiagramCanvas.Width  = w;
            DiagramCanvas.Height = h;

            var levels = _currentVm.Levels.OrderBy(l => l.Elevation).ToList();
            double minElev = levels.First().Elevation;
            double maxElev = levels.Last().Elevation;
            double range   = maxElev - minElev;
            if (range < 0.001) range = 1.0; // only one level — centre it

            double drawH = h - MarginTop - MarginBottom;

            foreach (var level in levels)
            {
                // Map elevation: low = bottom, high = top
                double t = range > 0.001 ? (level.Elevation - minElev) / range : 0.5;
                double y = MarginTop + (1.0 - t) * drawH;

                // Dashed horizontal line spanning the drawing area
                var line = new Line
                {
                    X1 = LabelColumnWidth,
                    X2 = w - 4,
                    Y1 = y,
                    Y2 = y,
                    Stroke          = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                DiagramCanvas.Children.Add(line);

                // Elevation label (metres) on the left
                double elevM = level.Elevation * 0.3048;
                string elevStr = elevM >= 0
                    ? $"+{elevM:0.00} m"
                    : $"{elevM:0.00} m";

                var elevLabel = new TextBlock
                {
                    Text       = elevStr,
                    FontSize   = 8,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    Width      = LabelColumnWidth - 4,
                    TextAlignment = System.Windows.TextAlignment.Right
                };
                Canvas.SetLeft(elevLabel, 0);
                Canvas.SetTop(elevLabel,  y - 9);
                DiagramCanvas.Children.Add(elevLabel);

                // Level name below the line
                var nameLabel = new TextBlock
                {
                    Text       = level.Name,
                    FontSize   = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    MaxWidth   = LabelColumnWidth - 4,
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(nameLabel, 0);
                Canvas.SetTop(nameLabel,  y + 2);
                DiagramCanvas.Children.Add(nameLabel);
            }
        }
    }
}
