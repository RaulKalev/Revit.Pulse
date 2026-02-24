using System;
using System.Collections.Generic;
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

        // Popup target: level name + kind ("line" | "text-above" | "text-below")
        private string _popupTargetLevel;
        private string _popupTargetKind;

        // Tracks visual elements by tag for highlighting
        private readonly Dictionary<string, FrameworkElement> _visualElements =
            new Dictionary<string, FrameworkElement>();
        private FrameworkElement _selectedVisual;
        private Brush            _selectedOriginalBrush;

        public DiagramPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => ApplyState();
            LevelPopup.Closed += (_, __) => ClearHighlight();
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
            {
                _currentVm.Levels.CollectionChanged  -= OnLevelsChanged;
                _currentVm.Panels.CollectionChanged  -= OnLevelsChanged;
            }

            _currentVm = DataContext as DiagramViewModel;

            if (_currentVm != null)
            {
                _currentVm.Levels.CollectionChanged  += OnLevelsChanged;
                _currentVm.Panels.CollectionChanged  += OnLevelsChanged;
            }

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
            ClearHighlight();
            _visualElements.Clear();
            DiagramCanvas.Children.Clear();

            if (_currentVm == null || _currentVm.Levels.Count == 0) return;

            double w = DiagramContent.ActualWidth;
            double h = DiagramContent.ActualHeight;
            if (w < 1 || h < 1) return;

            DiagramCanvas.Width  = w;
            DiagramCanvas.Height = h;

            var allLevels = _currentVm.Levels.OrderBy(l => l.Elevation).ToList();

            // Levels with a Deleted line are excluded from the Y-range.
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

                double t = (level.Elevation - minElev) / range;
                double y = MarginTop + (1.0 - t) * drawH;

                // ── Line ──────────────────────────────────────────────────
                if (lineState == LevelState.Visible)
                {
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
                    _visualElements[level.Name + "|line"] = line;
                }

                // ── Text above this line (this level's name) ──────────────
                if (_currentVm.GetTextAboveState(level.Name) == LevelState.Visible)
                {
                    var nameLabel = new TextBlock
                    {
                        Text         = level.Name,
                        FontSize     = 9,
                        FontWeight   = FontWeights.SemiBold,
                        Foreground   = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                        MaxWidth     = w - 16,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Tag          = level.Name + "|text-above",
                        Cursor       = Cursors.Hand
                    };
                    Canvas.SetRight(nameLabel, 8);
                    Canvas.SetTop(nameLabel, y - 13);
                    DiagramCanvas.Children.Add(nameLabel);
                    _visualElements[level.Name + "|text-above"] = nameLabel;
                }

                // ── Text below this line (previous level's name) ──────────
                if (i > 0)
                {
                    string prevName = null;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (_currentVm.GetLineState(rangeLevels[p].Name) != LevelState.Deleted)
                        {
                            prevName = rangeLevels[p].Name;
                            break;
                        }
                    }

                    if (prevName != null && _currentVm.GetTextBelowState(prevName) == LevelState.Visible)
                    {
                        var prevLabel = new TextBlock
                        {
                            Text         = prevName,
                            FontSize     = 9,
                            FontWeight   = FontWeights.SemiBold,
                            Foreground   = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                            MaxWidth     = w - 16,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Tag          = prevName + "|text-below",
                            Cursor       = Cursors.Hand
                        };
                        Canvas.SetRight(prevLabel, 8);
                        Canvas.SetTop(prevLabel, y + 2);
                        DiagramCanvas.Children.Add(prevLabel);
                        _visualElements[prevName + "|text-below"] = prevLabel;
                    }
                }
            }

            // ── Panels ──────────────────────────────────────────────────────────
            if (_currentVm.Panels.Count > 0)
            {
                // Build a quick elevation→Y lookup for the visible levels
                var yLookup = rangeLevels
                    .Select(l => new
                    {
                        l.Elevation,
                        Y = MarginTop + (1.0 - (l.Elevation - minElev) / range) * drawH
                    })
                    .ToList();

                foreach (var panel in _currentVm.Panels)
                {
                    if (!panel.Elevation.HasValue) continue;

                    double panelElev = panel.Elevation.Value;

                    // Floor: highest level at-or-below the panel
                    var floorEntry = yLookup.LastOrDefault(e2 => e2.Elevation <= panelElev + 0.001);
                    // Ceiling: lowest level strictly above the panel
                    var ceilEntry  = yLookup.FirstOrDefault(e2 => e2.Elevation > panelElev + 0.001);

                    if (floorEntry == null) continue;

                    double zoneBottom = floorEntry.Y;
                    double zoneTop    = ceilEntry != null ? ceilEntry.Y : MarginTop;

                    const double vPad  = 10.0;  // gap from level lines
                    double rectH = zoneBottom - zoneTop - vPad * 2;
                    if (rectH < 16) continue;

                    // Narrower rect — 56 % of canvas width, centered
                    const double hFrac = 0.22;
                    double rectLeft = w * hFrac;
                    double rectW    = w * (1.0 - hFrac * 2);
                    double rectTop  = zoneTop + vPad;

                    // ── Outer rectangle ──────────────────────────────────
                    var panelRect = new System.Windows.Shapes.Rectangle
                    {
                        Width            = rectW,
                        Height           = rectH,
                        Stroke           = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                        StrokeThickness  = 1,
                        Fill             = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                        RadiusX          = 3,
                        RadiusY          = 3,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(panelRect, rectLeft);
                    Canvas.SetTop(panelRect,  rectTop);
                    DiagramCanvas.Children.Add(panelRect);

                    // ── Loop output header ────────────────────────────────
                    // Total outputs to show: prefer config count, fall back to actual loops
                    int loopCount = panel.ConfigLoopCount > 0
                        ? panel.ConfigLoopCount
                        : panel.LoopNames.Count;
                    loopCount = Math.Min(loopCount, 16); // hard cap

                    const double headerH   = 22.0;
                    const double termSize  = 6.0;

                    if (loopCount > 0 && rectH > headerH + 8)
                    {
                        // Separator line below the header
                        var sep = new Line
                        {
                            X1               = rectLeft + 1,
                            X2               = rectLeft + rectW - 1,
                            Y1               = rectTop + headerH,
                            Y2               = rectTop + headerH,
                            Stroke           = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                            StrokeThickness  = 1,
                            IsHitTestVisible = false
                        };
                        DiagramCanvas.Children.Add(sep);

                        // Distribute terminals evenly across header width
                        double slotW  = (rectW - 8.0) / loopCount;
                        double termY  = rectTop + (headerH - termSize) / 2.0;

                        for (int li = 0; li < loopCount; li++)
                        {
                            double cx = rectLeft + 4.0 + slotW * (li + 0.5);

                            // Small filled square terminal
                            var term = new System.Windows.Shapes.Rectangle
                            {
                                Width            = termSize,
                                Height           = termSize,
                                Fill             = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(term, cx - termSize / 2.0);
                            Canvas.SetTop(term,  termY);
                            DiagramCanvas.Children.Add(term);

                            // (rotated loop name label drawn separately in the body area below)
                        }
                    }

                    // ── Panel name (centered in body below header) ────────
                    double bodyTop = (loopCount > 0 && rectH > headerH + 8)
                        ? rectTop + headerH + 2
                        : rectTop;
                    double bodyH   = rectTop + rectH - bodyTop;

                    // ── Rotated loop name labels (bottom-to-top, in body) ─
                    if (loopCount > 0 && bodyH > 8)
                    {
                        double slotW2        = (rectW - 8.0) / loopCount;
                        double labelAvailH   = bodyH - 4;      // visual height of each label
                        const double lblFont = 7.0;
                        double approxTextH   = lblFont * 1.35; // ~9.5 px — visual width of label

                        for (int li = 0; li < loopCount; li++)
                        {
                            double cx2 = rectLeft + 4.0 + slotW2 * (li + 0.5);

                            string fullLabel = li < panel.LoopNames.Count
                                ? panel.LoopNames[li]
                                : $"Loop {li + 1}";

                            var rotLabel = new TextBlock
                            {
                                Text             = fullLabel,
                                FontSize         = lblFont,
                                Foreground       = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                                IsHitTestVisible = false,
                                Width            = labelAvailH,
                                TextTrimming     = TextTrimming.CharacterEllipsis
                            };

                            // RenderTransform -90° around (0,0): visual bounds are
                            //   left = L, right = L+H,  top = T-W, bottom = T
                            // Want visual centered at cx2, visual top at bodyTop+2:
                            //   L = cx2 - approxTextH/2
                            //   T = (bodyTop + 2) + labelAvailH
                            rotLabel.RenderTransform = new System.Windows.Media.RotateTransform(-90);
                            Canvas.SetLeft(rotLabel, cx2 - approxTextH / 2.0);
                            Canvas.SetTop(rotLabel,  bodyTop + 2 + labelAvailH);
                            DiagramCanvas.Children.Add(rotLabel);
                        }
                    }

                    var panelLabel = new TextBlock
                    {
                        Text             = panel.Name,
                        FontSize         = 9,
                        FontWeight       = FontWeights.SemiBold,
                        Foreground       = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                        TextWrapping     = TextWrapping.Wrap,
                        TextAlignment    = TextAlignment.Center,
                        MaxWidth         = rectW - 8,
                        IsHitTestVisible = false
                    };
                    panelLabel.Measure(new System.Windows.Size(rectW - 8, double.PositiveInfinity));
                    double labelH = panelLabel.DesiredSize.Height;
                    Canvas.SetLeft(panelLabel, rectLeft + 4);
                    Canvas.SetTop(panelLabel,  bodyTop + Math.Max(0, (bodyH - labelH) / 2.0));
                    DiagramCanvas.Children.Add(panelLabel);
                }
            }

            UpdateRestoreButton();
        }

        // ── Level context popup ───────────────────────────────────────────

        private void DiagramCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentVm == null) return;

            var tag = (e.OriginalSource as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            int sep = tag.LastIndexOf('|');
            if (sep < 0) return;

            _popupTargetLevel = tag.Substring(0, sep);
            _popupTargetKind  = tag.Substring(sep + 1); // "line" | "text-above" | "text-below"

            // Highlight the clicked element
            if (_visualElements.TryGetValue(tag, out var visual))
                HighlightElement(visual);

            var state = GetStateForKind(_popupTargetLevel, _popupTargetKind);
            ConfigurePopup(_popupTargetLevel, _popupTargetKind, state);
            LevelPopup.IsOpen = true;
            e.Handled = true;
        }

        // ── Highlight ─────────────────────────────────────────────────────

        private static readonly SolidColorBrush AccentBrush =
            new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)); // Material Blue 300

        private void HighlightElement(FrameworkElement el)
        {
            ClearHighlight();
            if (el is Line ln)
            {
                _selectedOriginalBrush = ln.Stroke;
                ln.Stroke              = AccentBrush;
            }
            else if (el is TextBlock tb)
            {
                _selectedOriginalBrush = tb.Foreground;
                tb.Foreground          = AccentBrush;
            }
            _selectedVisual = el;
        }

        private void ClearHighlight()
        {
            if (_selectedVisual == null) return;
            if (_selectedVisual is Line ln)
                ln.Stroke          = _selectedOriginalBrush;
            else if (_selectedVisual is TextBlock tb)
                tb.Foreground      = _selectedOriginalBrush;
            _selectedVisual        = null;
            _selectedOriginalBrush = null;
        }

        private LevelState GetStateForKind(string levelName, string kind)
        {
            switch (kind)
            {
                case "line":       return _currentVm.GetLineState(levelName);
                case "text-above": return _currentVm.GetTextAboveState(levelName);
                default:           return _currentVm.GetTextBelowState(levelName);
            }
        }

        private void SetStateForKind(string levelName, string kind, LevelState state)
        {
            switch (kind)
            {
                case "line":       _currentVm.SetLineState(levelName, state);      break;
                case "text-above": _currentVm.SetTextAboveState(levelName, state); break;
                default:           _currentVm.SetTextBelowState(levelName, state); break;
            }
        }

        private void ConfigurePopup(string levelName, string kind, LevelState state)
        {
            string kindDisplay = kind == "line" ? "Line" : "Text";
            PopupLevelName.Text = $"{levelName} \u2014 {kindDisplay}";

            switch (state)
            {
                case LevelState.Visible:
                    PopupVisibilityButton.Content    = MakeButtonContent(PackIconKind.EyeOff, "Hide");
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = MakeButtonContent(PackIconKind.TrashCanOutline, "Delete");
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Hidden:
                    PopupVisibilityButton.Content    = MakeButtonContent(PackIconKind.Eye, "Show");
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Content        = MakeButtonContent(PackIconKind.TrashCanOutline, "Delete");
                    PopupDeleteButton.Visibility     = Visibility.Visible;
                    break;

                case LevelState.Deleted:
                    PopupVisibilityButton.Content    = MakeButtonContent(PackIconKind.Restore, "Restore");
                    PopupVisibilityButton.Visibility = Visibility.Visible;
                    PopupDeleteButton.Visibility     = Visibility.Collapsed;
                    break;
            }
        }

        private static StackPanel MakeButtonContent(PackIconKind iconKind, string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new PackIcon
            {
                Kind              = iconKind,
                Width             = 13,
                Height            = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text              = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 11
            });
            return sp;
        }

        private void PopupVisibility_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            var cur  = GetStateForKind(_popupTargetLevel, _popupTargetKind);
            var next = cur == LevelState.Visible ? LevelState.Hidden : LevelState.Visible;
            SetStateForKind(_popupTargetLevel, _popupTargetKind, next);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        private void PopupDelete_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            SetStateForKind(_popupTargetLevel, _popupTargetKind, LevelState.Deleted);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        // ── Restore panel ─────────────────────────────────────────────────

        private void UpdateRestoreButton()
        {
            if (_currentVm == null) { RestoreButton.Visibility = Visibility.Collapsed; return; }
            RestoreButton.Visibility = _currentVm.GetNonVisibleItems().Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
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

            foreach (var item in _currentVm.GetNonVisibleItems())
            {
                var color = item.State == LevelState.Hidden
                    ? Color.FromRgb(0xFF, 0xCC, 0x40)
                    : Color.FromRgb(0xFF, 0x55, 0x55);

                string kindDisplay;
                if (item.Kind == "line")
                    kindDisplay = "Line";
                else if (item.Kind == "text-above")
                    kindDisplay = "Text \u2191";
                else
                    kindDisplay = "Text \u2193";

                var row = new DockPanel { Margin = new Thickness(6, 1, 6, 1) };

                var restoreBtn = new Button
                {
                    Content           = "Restore",
                    Style             = (Style)FindResource("PulseActionButtonStyle"),
                    Padding           = new Thickness(8, 3, 8, 3),
                    FontSize          = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var captured = item;
                restoreBtn.Click += (s, args) =>
                {
                    SetStateForKind(captured.LevelName, captured.Kind, LevelState.Visible);
                    BuildRestoreList();
                    if (_currentVm.GetNonVisibleItems().Count == 0)
                        RestorePopup.IsOpen = false;
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
                };

                DockPanel.SetDock(restoreBtn, Dock.Right);
                row.Children.Add(restoreBtn);

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
