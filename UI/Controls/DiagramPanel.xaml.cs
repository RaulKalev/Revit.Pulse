using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Pulse.Core.Modules;
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

        // The level name the popup is currently targeting
        private string _popupTargetLevel;

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
            SetParentColumnWidth(_isExpanded ? ExpandedWidth : CollapsedWidth);

            if (_isExpanded)
            {
                DiagramContent.Visibility   = Visibility.Visible;
                HeaderTitleStack.Visibility = Visibility.Visible;
                CollapsedLabel.Visibility   = Visibility.Collapsed;
                ToggleIcon.Kind             = PackIconKind.ChevronRight;

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

            var allLevels = _currentVm.Levels.OrderBy(l => l.Elevation).ToList();

            // Deleted levels are excluded from min/max so they don't affect spacing.
            // Hidden levels are included in the range but not drawn.
            var rangelevels = allLevels
                .Where(l => _currentVm.GetLevelState(l.Name) != LevelState.Deleted)
                .ToList();

            if (rangelevels.Count == 0) return;

            double minElev = rangelevels.First().Elevation;
            double maxElev = rangelevels.Last().Elevation;
            double range   = maxElev - minElev;
            if (range < 0.001) range = 1.0;

            double drawH = h - MarginTop - MarginBottom;

            // Build an ordered list of visible/hidden levels for rendering
            for (int i = 0; i < rangelevels.Count; i++)
            {
                var level = rangelevels[i];
                var state = _currentVm.GetLevelState(level.Name);

                // Hidden levels are in the range but not drawn
                if (state == LevelState.Hidden) continue;

                double t = (level.Elevation - minElev) / range;
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
                    StrokeDashArray = new DoubleCollection { 10, 4, 4, 4 },
                    Tag             = level.Name,
                    Cursor          = Cursors.Hand
                };
                DiagramCanvas.Children.Add(line);

                // Current level name — above the line, right-aligned
                var nameLabel = new TextBlock
                {
                    Text          = level.Name,
                    FontSize      = 9,
                    FontWeight    = FontWeights.SemiBold,
                    Foreground    = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    Width         = w - 16,
                    TextAlignment = TextAlignment.Right,
                    TextTrimming  = TextTrimming.CharacterEllipsis,
                    Tag           = level.Name,
                    Cursor        = Cursors.Hand
                };
                Canvas.SetLeft(nameLabel, 8);
                Canvas.SetTop(nameLabel, y - 13);
                DiagramCanvas.Children.Add(nameLabel);

                // Previous (lower) level name — below this line, right-aligned, same gap.
                // Walk backwards through rangelevels to find the closest visible lower level.
                if (i > 0)
                {
                    string prevName = null;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (_currentVm.GetLevelState(rangelevels[p].Name) == LevelState.Visible)
                        {
                            prevName = rangelevels[p].Name;
                            break;
                        }
                    }

                    if (prevName != null)
                    {
                        var prevLabel = new TextBlock
                        {
                            Text          = prevName,
                            FontSize      = 9,
                            FontWeight    = FontWeights.SemiBold,
                            Foreground    = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                            Width         = w - 16,
                            TextAlignment = TextAlignment.Right,
                            TextTrimming  = TextTrimming.CharacterEllipsis,
                            Tag           = prevName,
                            Cursor        = Cursors.Hand
                        };
                        Canvas.SetLeft(prevLabel, 8);
                        Canvas.SetTop(prevLabel, y + 2);
                        DiagramCanvas.Children.Add(prevLabel);
                    }
                }
            }
        }

        // ── Popup ─────────────────────────────────────────────────────────

        private void DiagramCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentVm == null) return;

            // Walk up from the clicked element to find one with a level-name Tag
            var hit = e.OriginalSource as FrameworkElement;
            string levelName = null;
            while (hit != null && hit != DiagramCanvas)
            {
                if (hit.Tag is string s && !string.IsNullOrEmpty(s))
                {
                    levelName = s;
                    break;
                }
                hit = hit.Parent as FrameworkElement ?? (hit as FrameworkElement);
                // prevent looping on non-visual-tree parents
                if (hit is Canvas) break;
                hit = VisualTreeHelper.GetParent(hit) as FrameworkElement;
            }

            if (levelName == null) return;

            _popupTargetLevel = levelName;
            ConfigurePopup(levelName, _currentVm.GetLevelState(levelName));
            LevelPopup.IsOpen = true;
            e.Handled = true;
        }

        private void ConfigurePopup(string levelName, LevelState state)
        {
            PopupLevelName.Text = levelName;

            switch (state)
            {
                case LevelState.Visible:
                    PopupVisibilityButton.Content    = "Hide line";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = "Delete line";
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Hidden:
                    PopupVisibilityButton.Content    = "Show line";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = "Delete line";
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Deleted:
                    PopupVisibilityButton.Content    = "Restore line";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Visibility     = Visibility.Collapsed;
                    break;
            }
        }

        private void PopupVisibility_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            var current = _currentVm.GetLevelState(_popupTargetLevel);
            var next    = (current == LevelState.Visible) ? LevelState.Hidden : LevelState.Visible;

            _currentVm.SetLevelState(_popupTargetLevel, next);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        private void PopupDelete_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            _currentVm.SetLevelState(_popupTargetLevel, LevelState.Deleted);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }
    }
}
