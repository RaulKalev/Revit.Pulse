using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Controls
{
    public partial class DiagramPanel : UserControl
    {
        private const double ExpandedWidth  = 300;
        private const double CollapsedWidth = 32;
        private const double MarginTop      = 16;
        private const double MarginBottom   = 16;

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
            // Resize the parent ColumnDefinition rather than the UserControl itself
            SetParentColumnWidth(_isExpanded ? ExpandedWidth : CollapsedWidth);

            if (_isExpanded)
            {
                DiagramContent.Visibility   = Visibility.Visible;
                HeaderTitleStack.Visibility = Visibility.Visible;
                CollapsedLabel.Visibility   = Visibility.Collapsed;
                ToggleIcon.Kind             = PackIconKind.ChevronRight;

                // Layout hasn't run yet — defer the draw until after measure/arrange
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
            }
            else
            {
                DiagramContent.Visibility   = Visibility.Collapsed;
                HeaderTitleStack.Visibility = Visibility.Collapsed;
                CollapsedLabel.Visibility   = Visibility.Visible;
                ToggleIcon.Kind             = PackIconKind.ChevronLeft;
            }
        }

        /// <summary>Set the Width of the ColumnDefinition this panel sits in.</summary>
        private void SetParentColumnWidth(double width)
        {
            if (Parent is Grid parentGrid)
            {
                int col = Grid.GetColumn(this);
                if (col >= 0 && col < parentGrid.ColumnDefinitions.Count)
                    parentGrid.ColumnDefinitions[col].Width = new GridLength(width);
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

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        private void OnLevelsChanged(object sender, NotifyCollectionChangedEventArgs e)
            => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);

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
            if (range < 0.001) range = 1.0;

            double drawH = h - MarginTop - MarginBottom;

            for (int i = 0; i < levels.Count; i++)
            {
                double t = range > 0.001 ? (levels[i].Elevation - minElev) / range : 0.5;
                double y = MarginTop + (1.0 - t) * drawH;

                // Dashed line — long, gap, short, gap  (10:4:4:4)
                var line = new Line
                {
                    X1              = 8,
                    X2              = w - 4,
                    Y1              = y,
                    Y2              = y,
                    Stroke          = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 10, 4, 4, 4 }
                };
                DiagramCanvas.Children.Add(line);

                // Current level name — above the line, right-aligned
                var nameLabel = new TextBlock
                {
                    Text          = levels[i].Name,
                    FontSize      = 9,
                    FontWeight    = FontWeights.SemiBold,
                    Foreground    = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    Width         = w - 16,
                    TextAlignment = TextAlignment.Right,
                    TextTrimming  = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(nameLabel, 8);
                Canvas.SetTop(nameLabel, y - 13);
                DiagramCanvas.Children.Add(nameLabel);

                // Previous (lower) level name — below this line, right-aligned, same gap
                if (i > 0)
                {
                    var prevLabel = new TextBlock
                    {
                        Text          = levels[i - 1].Name,
                        FontSize      = 9,
                        FontWeight    = FontWeights.SemiBold,
                        Foreground    = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                        Width         = w - 16,
                        TextAlignment = TextAlignment.Right,
                        TextTrimming  = TextTrimming.CharacterEllipsis
                    };
                    Canvas.SetLeft(prevLabel, 8);
                    Canvas.SetTop(prevLabel, y + 2);
                    DiagramCanvas.Children.Add(prevLabel);
                }
            }
        }
    }
}
