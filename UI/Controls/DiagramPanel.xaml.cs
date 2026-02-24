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

        private bool _isExpanded = false;

        // Popup target: level name + "line" or "text"
        private string _popupTargetLevel;
        private string _popupTargetKind;

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

            // Levels whose LINE is Deleted are excluded from the Y-range calculation.
            var rangeLevels = allLevels
                .Where(l => _currentVm.GetLineState(l.Name) != LevelState.Deleted)
                .ToList();

            if (rangeLevels.Count == 0) return;

            double minElev = rangeLevels.First().Elevation;
            double maxElev = rangeLevels.Last().Elevation;
            double range   = maxElev - minElev;
            if (range < 0.001) range = 1.0;

            double drawH = h - MarginTop - MarginBottom;

            for (int i = 0; i < rangeLevels.Count; i++)
            {
                var level     = rangeLevels[i];
                var lineState = _currentVm.GetLineState(level.Name);
                var textState = _currentVm.GetTextState(level.Name);

                double t = (level.Elevation - minElev) / range;
                double y = MarginTop + (1.0 - t) * drawH;

                // ── Line ──────────────────────────────────────────────────
                if (lineState == LevelState.Visible)
                {
                    // Transparent wide hit area
                    var hitLine = new Line
                    {
                        X1              = 8,
                        X2              = w - 4,
                        Y1              = y,
                        Y2              = y,
                        Stroke          = Brushes.Transparent,
                        StrokeThickness = 10,
                        Tag             = level.Name + "|line",
                        Cursor          = Cursors.Hand
                    };
                    DiagramCanvas.Children.Add(hitLine);

                    // Visual dashed line — long, gap, short, gap (10:4:4:4)
                    var line = new Line
                    {
                        X1               = 8,
                        X2               = w - 4,
                        Y1               = y,
                        Y2               = y,
                        Stroke           = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                        StrokeThickness  = 1,
                        StrokeDashArray  = new DoubleCollection { 10, 4, 4, 4 },
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(line);
                }

                // ── Text above (current level name) ───────────────────────
                if (textState == LevelState.Visible)
                {
                    var nameLabel = new TextBlock
                    {
                        Text          = level.Name,
                        FontSize      = 9,
                        FontWeight    = FontWeights.SemiBold,
                        Foreground    = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                        Width         = w - 16,
                        TextAlignment = TextAlignment.Right,
                        TextTrimming  = TextTrimming.CharacterEllipsis,
                        Tag           = level.Name + "|text",
                        Cursor        = Cursors.Hand
                    };
                    Canvas.SetLeft(nameLabel, 8);
                    Canvas.SetTop(nameLabel, y - 13);
                    DiagramCanvas.Children.Add(nameLabel);
                }

                // ── Text below (previous level name) ─────────────────────
                if (i > 0)
                {
                    // Find the closest non-Deleted level below
                    string prevName = null;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (_currentVm.GetLineState(rangeLevels[p].Name) != LevelState.Deleted)
                        {
                            prevName = rangeLevels[p].Name;
                            break;
                        }
                    }

                    if (prevName != null && _currentVm.GetTextState(prevName) == LevelState.Visible)
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
                            Tag           = prevName + "|text",
                            Cursor        = Cursors.Hand
                        };
                        Canvas.SetLeft(prevLabel, 8);
                        Canvas.SetTop(prevLabel, y + 2);
                        DiagramCanvas.Children.Add(prevLabel);
                    }
                }
            }

            UpdateRestoreButton();
        }

        // ── Level context popup ───────────────────────────────────────────

        private void DiagramCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentVm == null) return;

            // Tags are "LevelName|line" or "LevelName|text"
            var tag = (e.OriginalSource as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            int sep = tag.LastIndexOf('|');
            if (sep < 0) return;

            _popupTargetLevel = tag.Substring(0, sep);
            _popupTargetKind  = tag.Substring(sep + 1);  // "line" or "text"

            var state = _popupTargetKind == "line"
                ? _currentVm.GetLineState(_popupTargetLevel)
                : _currentVm.GetTextState(_popupTargetLevel);

            ConfigurePopup(_popupTargetLevel, _popupTargetKind, state);
            LevelPopup.IsOpen = true;
            e.Handled = true;
        }

        private void ConfigurePopup(string levelName, string kind, LevelState state)
        {
            string kindDisplay = kind == "line" ? "Line" : "Text";
            PopupLevelName.Text = $"{levelName} — {kindDisplay}";

            switch (state)
            {
                case LevelState.Visible:
                    PopupVisibilityButton.Content    = "Hide";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = "Delete";
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Hidden:
                    PopupVisibilityButton.Content    = "Show";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = "Delete";
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Deleted:
                    PopupVisibilityButton.Content    = "Restore";
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Visibility     = Visibility.Collapsed;
                    break;
            }
        }

        private void PopupVisibility_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            if (_popupTargetKind == "line")
            {
                var cur  = _currentVm.GetLineState(_popupTargetLevel);
                var next = cur == LevelState.Visible ? LevelState.Hidden : LevelState.Visible;
                _currentVm.SetLineState(_popupTargetLevel, next);
            }
            else
            {
                var cur  = _currentVm.GetTextState(_popupTargetLevel);
                var next = cur == LevelState.Visible ? LevelState.Hidden : LevelState.Visible;
                _currentVm.SetTextState(_popupTargetLevel, next);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        private void PopupDelete_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            if (_popupTargetKind == "line")
                _currentVm.SetLineState(_popupTargetLevel, LevelState.Deleted);
            else
                _currentVm.SetTextState(_popupTargetLevel, LevelState.Deleted);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        // ── Restore panel ─────────────────────────────────────────────────

        private void UpdateRestoreButton()
        {
            if (_currentVm == null)
            {
                RestoreButton.Visibility = Visibility.Collapsed;
                return;
            }
            RestoreButton.Visibility = _currentVm.GetNonVisibleItems().Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            BuildRestoreList();
            RestorePopup.IsOpen = true;
        }

        private void BuildRestoreList()
        {
            RestoreList.Children.Clear();
            if (_currentVm == null) return;

            var items = _currentVm.GetNonVisibleItems();
            foreach (var item in items)
            {
                // Yellow = Hidden, Red = Deleted
                var color = item.State == LevelState.Hidden
                    ? Color.FromRgb(0xFF, 0xCC, 0x40)
                    : Color.FromRgb(0xFF, 0x55, 0x55);

                var row = new DockPanel { Margin = new Thickness(6, 1, 6, 1) };

                var restoreBtn = new Button
                {
                    Content  = "Restore",
                    Style    = (Style)FindResource("PulseActionButtonStyle"),
                    Padding  = new Thickness(8, 3, 8, 3),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var captured = item;
                restoreBtn.Click += (s, args) =>
                {
                    if (captured.Kind == "line")
                        _currentVm.SetLineState(captured.LevelName, LevelState.Visible);
                    else
                        _currentVm.SetTextState(captured.LevelName, LevelState.Visible);

                    // Rebuild the list; close popup if empty
                    BuildRestoreList();
                    if (_currentVm.GetNonVisibleItems().Count == 0)
                        RestorePopup.IsOpen = false;

                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
                };

                DockPanel.SetDock(restoreBtn, Dock.Right);
                row.Children.Add(restoreBtn);

                string kindDisplay = captured.Kind == "line" ? "Line" : "Text";
                var label = new TextBlock
                {
                    Text              = $"{captured.LevelName} \u2014 {kindDisplay}",
                    FontSize          = 10,
                    Foreground        = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding           = new Thickness(4, 0, 6, 0),
                    TextTrimming      = TextTrimming.CharacterEllipsis
                };
                row.Children.Add(label);

                RestoreList.Children.Add(row);
            }
        }
    }
}
