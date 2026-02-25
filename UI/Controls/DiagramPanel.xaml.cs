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
using Pulse.Core.Settings;
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

        // ── Zoom ─────────────────────────────────────────────────────────
        private const double ZoomMin  = 0.15;
        private const double ZoomMax  = 6.0;
        private const double ZoomStep = 0.12;   // fractional step per wheel tick
        private double _zoom = 1.0;
        private readonly ScaleTransform     _zoomST = new ScaleTransform(1, 1);
        private readonly TranslateTransform _zoomTT = new TranslateTransform(0, 0);

        // ── Middle-mouse pan ──────────────────────────────────────────
        private bool   _isPanning;
        private Point  _panStart;
        private double _panStartX;
        private double _panStartY;

        // ── Move mode ─────────────────────────────────────────────────────
        private bool      _inMoveMode;
        private LevelInfo _movingLevel;
        private double    _moveOriginalElev;
        // Cached after each DrawLevels — used by Move mouse handler
        private double _drawMinElev;
        private double _drawRange;
        private double _drawDrawH;

        // Popup target: level name + kind ("line" | "text-above" | "text-below")
        private string _popupTargetLevel;
        private string _popupTargetKind;

        // Tracks visual elements by tag for highlighting
        private readonly Dictionary<string, FrameworkElement> _visualElements =
            new Dictionary<string, FrameworkElement>();
        private FrameworkElement _selectedVisual;
        private Brush            _selectedOriginalBrush;

        // Custom symbol library (loaded once per VM change)
        private List<CustomSymbolDefinition> _symbolLibrary;

        // Canvas drawing settings (loaded from disk, editable via popup)
        private DiagramCanvasSettings _canvasSettings = new DiagramCanvasSettings();

        // ── Repetition compression ────────────────────────────────────────

        private struct WireSlot
        {
            public bool   IsDots;        // true = ··· placeholder
            public string DeviceType;    // meaningful when IsDots == false
            public int    AddressIndex;  // index into loopInfo.DeviceAddresses / DeviceTypesByAddress
        }

        /// <summary>
        /// Compresses a wire row of <paramref name="count"/> raw device types (starting at
        /// <paramref name="offset"/> in <paramref name="allTypes"/>) into a slot list.
        /// Runs of 4+ consecutive identical types are collapsed to: first + dots + last.
        /// </summary>
        private static List<WireSlot> BuildCompressedRow(
            IReadOnlyList<string> allTypes, int offset, int count)
        {
            var raw = new string[count];
            for (int i = 0; i < count; i++)
                raw[i] = (offset + i < allTypes.Count) ? allTypes[offset + i] : null;

            var result = new List<WireSlot>(count);
            int j = 0;
            while (j < count)
            {
                string type     = raw[j];
                int    runStart = j;
                // Advance while same type (null == null is OK for string ==)
                while (j < count && raw[j] == type) j++;
                int runLen = j - runStart;

                if (runLen >= 4)
                {
                    result.Add(new WireSlot { DeviceType = raw[runStart], AddressIndex = offset + runStart }); // first
                    result.Add(new WireSlot { IsDots     = true,          AddressIndex = -1 });                // ···
                    result.Add(new WireSlot { DeviceType = raw[j - 1],   AddressIndex = offset + j - 1 });    // last
                }
                else
                {
                    for (int k = runStart; k < j; k++)
                        result.Add(new WireSlot { DeviceType = raw[k], AddressIndex = offset + k });
                }
            }
            return result;
        }

        public DiagramPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => ApplyState();

            // Apply the zoom transform group to the canvas once on construction.
            var tg = new TransformGroup();
            tg.Children.Add(_zoomST);
            tg.Children.Add(_zoomTT);
            DiagramCanvas.RenderTransform = tg;
            LevelPopup.Closed += (_, __) => ClearHighlight();
            LoopPopup.Closed  += (_, __) =>
            {
                if (_currentVm != null) _currentVm.SelectedLoopKey = null;
                // Don't call DrawLevels here — FlipStateChanged/AddLine already triggers it.
                // Only redraw if no action was taken (plain close/dismiss).
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
            };
            PanelPopup.Closed += (_, __) =>
            {
                _selectedPanelName = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
            };
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
                DiagramContent.Visibility        = Visibility.Visible;
                HeaderTitleStack.Visibility      = Visibility.Visible;
                CollapsedLabel.Visibility        = Visibility.Collapsed;
                CanvasSettingsButton.Visibility  = Visibility.Visible;
                ToggleIcon.Kind                  = PackIconKind.ChevronRight;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
            }
            else
            {
                DiagramContent.Visibility        = Visibility.Collapsed;
                HeaderTitleStack.Visibility      = Visibility.Collapsed;
                CollapsedLabel.Visibility        = Visibility.Visible;
                CanvasSettingsButton.Visibility  = Visibility.Collapsed;
                ToggleIcon.Kind                  = PackIconKind.ChevronLeft;
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
                _currentVm.FlipStateChanged           = null;
            }

            _currentVm = DataContext as DiagramViewModel;

            if (_currentVm != null)
            {
                _currentVm.Levels.CollectionChanged  += OnLevelsChanged;
                _currentVm.Panels.CollectionChanged  += OnLevelsChanged;
                _currentVm.FlipStateChanged           = () => Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded, (System.Action)DrawLevels);
            }

            // Refresh symbol library whenever the data context changes
            _symbolLibrary = CustomSymbolLibraryService.Load();
            _canvasSettings = DiagramCanvasSettingsService.Load();

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);
        }

        private void OnLevelsChanged(object sender, NotifyCollectionChangedEventArgs e)
            => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)DrawLevels);

        // ── SizeChanged ───────────────────────────────────────────────────

        private void DiagramContent_SizeChanged(object sender, SizeChangedEventArgs e)
            => DrawLevels();

        // ── Zoom handlers ─────────────────────────────────────────────────

        private void DiagramContent_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Always mark handled so the inner ScrollViewer never consumes wheel events.
            e.Handled = true;
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;

            // Mouse position relative to DiagramContent (the viewport)
            Point mouse = e.GetPosition(DiagramContent);

            double oldZoom = _zoom;
            double delta   = e.Delta > 0 ? ZoomStep : -ZoomStep;
            _zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoom + _zoom * delta));

            // Zoom towards the cursor:
            // new_translate = mouse - (mouse - old_translate) * (new_zoom / old_zoom)
            double ratio    = _zoom / oldZoom;
            _zoomST.ScaleX  = _zoom;
            _zoomST.ScaleY  = _zoom;
            _zoomTT.X       = mouse.X - (mouse.X - _zoomTT.X) * ratio;
            _zoomTT.Y       = mouse.Y - (mouse.Y - _zoomTT.Y) * ratio;

            e.Handled = true;
        }

        private void DiagramContent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                // Double-click → reset zoom & pan
                if (e.ClickCount == 2)
                {
                    _isPanning     = false;
                    _zoom          = 1.0;
                    _zoomST.ScaleX = 1.0;
                    _zoomST.ScaleY = 1.0;
                    _zoomTT.X      = 0.0;
                    _zoomTT.Y      = 0.0;
                    DiagramContent.ReleaseMouseCapture();
                    DiagramContent.Cursor = Cursors.Arrow;
                    e.Handled = true;
                    return;
                }

                // Single middle-button press → start panning
                _isPanning  = true;
                _panStart   = e.GetPosition(DiagramContent);
                _panStartX  = _zoomTT.X;
                _panStartY  = _zoomTT.Y;
                DiagramContent.CaptureMouse();
                DiagramContent.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        // ── Drawing ───────────────────────────────────────────────────────

        private void DrawLevels()
        {
            ClearHighlight();
            _visualElements.Clear();
            DiagramCanvas.Children.Clear();

            // Reload symbol library and canvas settings on each draw so that changes
            // made since the last refresh (new symbols, mapping edits) are always reflected.
            _symbolLibrary  = CustomSymbolLibraryService.Load();
            _canvasSettings = DiagramCanvasSettingsService.Load();

            if (_currentVm == null || _currentVm.Levels.Count == 0) return;

            double w = DiagramContent.ActualWidth;
            double h = DiagramContent.ActualHeight;

            // Reset side-group cache each full redraw
            _loopSideGroupsCache.Clear();
            if (w < 1 || h < 1) return;

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

            // ── Pre-pass: compute required canvas width to accommodate all loop extents ──
            // Right-side (flipped) loops grow outward from laneX and can exceed w.
            double totalW = w;
            foreach (var panelPre in _currentVm.Panels)
            {
                if (!panelPre.Elevation.HasValue) continue;
                int lcPre = Math.Min(
                    panelPre.ConfigLoopCount > 0 ? panelPre.ConfigLoopCount : panelPre.LoopInfos.Count,
                    16);
                if (lcPre == 0) continue;
                const double lsWPre  = 200.0 - 52.0; // leftSecW
                const double rWPre   = 10.0;          // wireRight
                double slWPre  = lsWPre / lcPre;
                double rLPre   = (w - 200.0) / 2.0;
                for (int liPre = 0; liPre < Math.Min(panelPre.LoopInfos.Count, lcPre); liPre++)
                {
                    var infPre = panelPre.LoopInfos[liPre];
                    if (infPre.Levels == null || infPre.Levels.Count == 0) continue;
                    bool flPre = _currentVm.IsLoopFlipped(panelPre.Name, infPre.Name);
                    int  totPre = infPre.DeviceTypesByAddress.Count;
                    if (totPre == 0) continue;
                    int  wcPre  = _currentVm.GetLoopWireCount(panelPre.Name, infPre.Name);
                    int  mprPre = (int)Math.Ceiling((double)totPre / wcPre);
                    double lxPre = rLPre + slWPre * (liPre + 0.5);
                    double bePre = flPre ? (w - rWPre) : 10.0; // wireLeft = 10
                    double s0Pre = Math.Abs(lxPre - bePre);
                    double dsPre = (_canvasSettings.DeviceSpacingPx > 0)
                        ? _canvasSettings.DeviceSpacingPx
                        : (mprPre > 0 ? s0Pre / (mprPre + 1) : s0Pre);
                    double fePre = flPre
                        ? lxPre + dsPre * (mprPre + 1)
                        : lxPre - dsPre * (mprPre + 1);
                    double neededW = flPre ? fePre + rWPre : w; // right-side extends canvas
                    totalW = Math.Max(totalW, neededW);
                }
            }
            DiagramCanvas.Width = totalW;

            // Cache for move-mode Y↔elevation conversion
            _drawMinElev = minElev;
            _drawRange   = range;
            _drawDrawH   = drawH;

            for (int i = 0; i < rangeLevels.Count; i++)
            {
                var level     = rangeLevels[i];
                var lineState = _currentVm.GetLineState(level.Name);

                double t = (level.Elevation - minElev) / range;
                double y = MarginTop + (1.0 - t) * drawH;

                // ── Line ──────────────────────────────────────────────────
                bool isMovingLevel = _inMoveMode && _movingLevel != null
                                      && level.Name == _movingLevel.Name;

                if (lineState == LevelState.Visible || isMovingLevel)
                {
                    // Hit-test overlay only for normally-visible (clickable) lines
                    if (lineState == LevelState.Visible)
                    {
                        var hitLine = new Line
                        {
                            X1              = 8,
                            X2              = totalW - 4,
                            Y1              = y,
                            Y2              = y,
                            Stroke          = Brushes.Transparent,
                            StrokeThickness = 10,
                            Tag             = level.Name + "|line",
                            Cursor          = Cursors.Hand
                        };
                        DiagramCanvas.Children.Add(hitLine);
                    }

                    var lineVisualBrush = isMovingLevel
                        ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07)) // amber
                        : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                    double lineThickness = isMovingLevel ? 2.0 : 1.0;

                    var line = new Line
                    {
                        X1               = 8,
                        X2               = totalW - 4,
                        Y1               = y,
                        Y2               = y,
                        Stroke           = lineVisualBrush,
                        StrokeThickness  = lineThickness,
                        IsHitTestVisible = false
                    };
                    if (!isMovingLevel)
                        line.StrokeDashArray = new DoubleCollection { 10, 4, 4, 4 };
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

                    // Fixed panel size — does not scale with canvas
                    const double panelFixedW = 200.0;
                    const double panelFixedH = 120.0;

                    // Skip zone if it is too narrow to contain the panel
                    if (zoneBottom - zoneTop < panelFixedH + 8) continue;

                    double rectW    = panelFixedW;
                    double rectH    = panelFixedH;
                    double rectLeft = (w - rectW) / 2.0;          // horizontally centered

                    // Per-panel user settings (name, outCount, supply) — needed before rectTop
                    var panelCfg = GetPanelCfg(panel.Name);

                    // Height of everything drawn below the panel bottom edge:
                    //   • No outputs            → 0   (10 px gap directly from panel bottom)
                    //   • Outputs, no labels    → 14  (output cell height)
                    //   • Outputs with N labels → 14 + 8 + (N-1)*12  (cells + L-shaped lines)
                    int preLabeledCount = 0;
                    if (panelCfg.OutCount > 0)
                        for (int pi = 0; pi < panelCfg.OutCount; pi++)
                            if (panelCfg.OutputLabels.Count > pi
                                && !string.IsNullOrWhiteSpace(panelCfg.OutputLabels[pi]))
                                preLabeledCount++;

                    double belowPanelH = panelCfg.OutCount == 0 ? 0.0
                                       : preLabeledCount == 0   ? 14.0
                                       : 14.0 + 8.0 + (preLabeledCount - 1) * 12.0;

                    double rectTop  = zoneBottom - 10.0 - belowPanelH - rectH;

                    // Left section (loops + body) and right section (power/battery)
                    const double rightSecW = 52.0;
                    double leftSecW        = rectW - rightSecW;

                    // Common brushes
                    bool isPanelSelected = _selectedPanelName == panel.Name;
                    var panelStroke = isPanelSelected
                        ? new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xC3, 0xF7))
                        : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                    var panelDim    = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                    var panelDimmer = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

                    // Panel-local line helper (separate name from the loop-wire helper defined later)
                    void PLine(Brush br, double x1, double y1, double x2, double y2, double thick = 1.0) =>
                        DiagramCanvas.Children.Add(new Line
                        {
                            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                            Stroke = br, StrokeThickness = thick, IsHitTestVisible = false
                        });

                    // ── Outer rectangle ──────────────────────────────────
                    var panelRect = new System.Windows.Shapes.Rectangle
                    {
                        Width            = rectW,
                        Height           = rectH,
                        Stroke           = panelStroke,
                        StrokeThickness  = 1,
                        Fill             = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                        RadiusX          = 3,
                        RadiusY          = 3,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(panelRect, rectLeft);
                    Canvas.SetTop(panelRect,  rectTop);
                    DiagramCanvas.Children.Add(panelRect);

                    // ── Clickable transparent overlay — opens the panel editor popup ──
                    var panelClickTarget = new System.Windows.Shapes.Rectangle
                    {
                        Width            = rectW,
                        Height           = rectH,
                        Fill             = Brushes.Transparent,
                        IsHitTestVisible = true,
                        Cursor           = Cursors.Hand,
                        Tag              = panel.Name
                    };
                    Canvas.SetLeft(panelClickTarget, rectLeft);
                    Canvas.SetTop(panelClickTarget,  rectTop);
                    // Capture loop-local values for the lambda
                    var _pName   = panel.Name;
                    var _pLeft   = rectLeft;
                    var _pTop    = rectTop;
                    var _pW      = rectW;
                    panelClickTarget.MouseLeftButtonUp += (_, e) =>
                    {
                        _selectedPanelName = _pName;
                        DrawLevels();
                        ShowPanelEditPopup(_pName, _pLeft + _pW / 2, _pTop);
                        e.Handled = true;
                    };
                    DiagramCanvas.Children.Add(panelClickTarget);

                    // ── Loop output header (left section only) ───────────
                    int loopCount = panel.ConfigLoopCount > 0
                        ? panel.ConfigLoopCount
                        : panel.LoopInfos.Count;
                    loopCount = Math.Min(loopCount, 16);

                    const double lblFont   = 7.0;
                    // Size the header to exactly fit the longest expected loop label ("Loop 10") + margin
                    var _headerProbe = new TextBlock { Text = "Loop 10", FontSize = lblFont };
                    _headerProbe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    double headerH     = _headerProbe.DesiredSize.Width + 8.0;
                    double approxLineH = lblFont * 1.55;

                    if (loopCount > 0 && rectH > headerH + 12)
                    {
                        double slotW = leftSecW / loopCount;

                        // Bottom border of header (left section only)
                        PLine(panelDim,
                              rectLeft + 1,        rectTop + headerH,
                              rectLeft + leftSecW,  rectTop + headerH);

                        for (int li = 0; li < loopCount; li++)
                        {
                            double cellLeft  = rectLeft + slotW * li;
                            double cellRight = cellLeft + slotW;

                            if (li < loopCount - 1)
                                PLine(panelDimmer, cellRight, rectTop + 1, cellRight, rectTop + headerH - 1);

                            string rawName   = li < panel.LoopInfos.Count ? panel.LoopInfos[li].Name : $"{li + 1}";
                            string fullLabel = rawName.StartsWith("Loop ", StringComparison.OrdinalIgnoreCase)
                                ? rawName : $"Loop {rawName}";

                            double lw = headerH - 4;
                            var rotLabel = new TextBlock
                            {
                                Text             = fullLabel,
                                FontSize         = lblFont,
                                Foreground       = panelStroke,
                                IsHitTestVisible = false,
                                Width            = lw,
                                TextTrimming     = TextTrimming.CharacterEllipsis
                            };
                            double cx = cellLeft + slotW / 2.0;
                            rotLabel.RenderTransform = new System.Windows.Media.RotateTransform(-90);
                            Canvas.SetLeft(rotLabel, cx - approxLineH / 2.0);
                            Canvas.SetTop(rotLabel,  rectTop + 2 + lw);
                            DiagramCanvas.Children.Add(rotLabel);
                        }
                    }

                    // ── Right section (power / battery) ──────────────────
                    double rsLeft = rectLeft + leftSecW;
                    double rsCX   = rsLeft + rightSecW / 2.0;

                    // Vertical divider between sections
                    PLine(panelDim, rsLeft, rectTop + 1, rsLeft, rectTop + rectH - 1);

                    // ---- Battery symbol (XAML UserControl, centered in top half) ----
                    double batAreaCY = rectTop + rectH / 4.0;
                    var batSymbol = new BatterySymbol
                    {
                        StrokeBrush = panelStroke,
                        DimBrush    = panelDim
                    };
                    Canvas.SetLeft(batSymbol, rsLeft);
                    Canvas.SetTop(batSymbol,  batAreaCY - 30); // 30 = half control height (60/2)
                    DiagramCanvas.Children.Add(batSymbol);

                    // ---- Horizontal divider at panel vertical centre (battery / PSU boundary) ----
                    double rsDivY = rectTop + rectH / 2.0;
                    PLine(panelDim, rsLeft + 1, rsDivY, rsLeft + rightSecW - 1, rsDivY);

                    // ---- PSU / diagonal section (XAML UserControl, fills lower half) ----
                    double diagTop  = rsDivY + 1;
                    double diagBoxH = (rectTop + rectH - 2) - diagTop;
                    var psuSymbol = new PSUSymbol
                    {
                        StrokeBrush = panelStroke,
                        DimBrush    = panelDim,
                        Width       = rightSecW - 4,
                        Height      = diagBoxH
                    };
                    Canvas.SetLeft(psuSymbol, rsLeft + 2);
                    Canvas.SetTop(psuSymbol,  diagTop);
                    DiagramCanvas.Children.Add(psuSymbol);

                    // ── Power connection: from right panel edge → L-path → IEC earth symbol + label ──
                    double pwrY        = rsDivY + diagBoxH * 0.45; // midway in diagonal section
                    double gndX0       = rectLeft + rectW;          // panel right edge
                    double gndHorizLen = 14.0;                      // horizontal lead
                    double gndVertLen  = 14.0;                      // vertical drop
                    double gndX1       = gndX0 + gndHorizLen;      // elbow / earth symbol centre X
                    double gndY1       = pwrY  + gndVertLen;        // top bar of earth symbol

                    // Horizontal lead from panel edge to elbow
                    PLine(panelDim, gndX0, pwrY, gndX1, pwrY);
                    // Vertical drop from elbow to earth symbol
                    PLine(panelDim, gndX1, pwrY, gndX1, gndY1);
                    // IEC earth symbol — three decreasing bars below the vertical
                    PLine(panelStroke, gndX1 - 6, gndY1,     gndX1 + 6, gndY1);     // bar 1 (widest)
                    PLine(panelStroke, gndX1 - 4, gndY1 + 4, gndX1 + 4, gndY1 + 4); // bar 2
                    PLine(panelStroke, gndX1 - 2, gndY1 + 8, gndX1 + 2, gndY1 + 8); // bar 3 (narrowest)

                    // "Toide 230V" label + stub line at bottom-right corner of panel
                    double toideY      = rectTop + rectH - 1;    // 1px up so line bottom aligns with panel bottom edge
                    double toideLineX0 = rectLeft + rectW - 3;   // overlap panel stroke to eliminate gap
                    double toideLabelX = rectLeft + rectW + 5;   // text starts 5px from panel edge

                    var pwrLabel = new TextBlock
                    {
                        Text = panelCfg.Supply, FontSize = 6,
                        Foreground = panelDim, IsHitTestVisible = false
                    };
                    pwrLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    double toideStubLen = (toideLabelX - toideLineX0) + pwrLabel.DesiredSize.Width + 10.0;

                    PLine(panelDim, toideLineX0, toideY, toideLineX0 + toideStubLen, toideY);

                    Canvas.SetLeft(pwrLabel, toideLabelX);
                    Canvas.SetTop(pwrLabel,  toideY - pwrLabel.DesiredSize.Height - 1);
                    DiagramCanvas.Children.Add(pwrLabel);

                    // ── Body: subtitle lines + panel name (left section, below header) ──
                    double bodyTop = (loopCount > 0 && rectH > headerH + 12)
                        ? rectTop + headerH + 2
                        : rectTop;
                    double bodyH = (rectTop + rectH) - bodyTop;

                    // Sub-title lines from per-panel settings
                    var _nameParts = panelCfg.Name.Split('\n');
                    string _sub1Text = _nameParts.Length > 0 ? _nameParts[0] : string.Empty;
                    string _sub2Text = _nameParts.Length > 1 ? _nameParts[1] : string.Empty;

                    // Sub-title 1
                    var sub1 = new TextBlock
                    {
                        Text = _sub1Text,
                        FontSize = 5.5, FontWeight = FontWeights.SemiBold,
                        Foreground = panelDim,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = leftSecW - 4, IsHitTestVisible = false,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    sub1.Measure(new System.Windows.Size(leftSecW - 4, double.PositiveInfinity));
                    Canvas.SetLeft(sub1, rectLeft + 2);
                    Canvas.SetTop(sub1,  bodyTop + 3);
                    DiagramCanvas.Children.Add(sub1);

                    // Sub-title 2
                    var sub2 = new TextBlock
                    {
                        Text = _sub2Text,
                        FontSize = 5.5,
                        Foreground = panelDim,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = leftSecW - 4, IsHitTestVisible = false,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    sub2.Measure(new System.Windows.Size(leftSecW - 4, double.PositiveInfinity));
                    Canvas.SetLeft(sub2, rectLeft + 2);
                    Canvas.SetTop(sub2,  bodyTop + 11);
                    DiagramCanvas.Children.Add(sub2);

                    // Main panel name (larger, bold)
                    double nameAreaTop = bodyTop + 22;
                    double nameAreaH   = bodyH - 22;
                    var panelLabel = new TextBlock
                    {
                        Text          = panel.Name,
                        FontSize      = 11,
                        FontWeight    = FontWeights.Bold,
                        Foreground    = panelStroke,
                        TextWrapping  = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth      = leftSecW - 8,
                        IsHitTestVisible = false
                    };
                    panelLabel.Measure(new System.Windows.Size(leftSecW - 8, double.PositiveInfinity));
                    double labelH = panelLabel.DesiredSize.Height;
                    Canvas.SetLeft(panelLabel, rectLeft + 4);
                    Canvas.SetTop(panelLabel,  nameAreaTop + Math.Max(0, (nameAreaH - labelH) / 2.0));
                    DiagramCanvas.Children.Add(panelLabel);

                    // ── Output cells below bottom-left of panel ───────────
                    int outCount = panelCfg.OutCount;
                    const double outCellW  = 11.0;
                    const double outCellH  = 14.0;
                    double outStartX       = rectLeft + 4;
                    double outY            = rectTop + rectH;  // flush with panel bottom

                    for (int oi = 0; oi < outCount; oi++)
                    {
                        double ocx = outStartX + oi * outCellW;
                        var outRect = new System.Windows.Shapes.Rectangle
                        {
                            Width = outCellW, Height = outCellH,
                            Stroke = panelDim, StrokeThickness = 1,
                            Fill = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(outRect, ocx);
                        Canvas.SetTop(outRect,  outY);
                        DiagramCanvas.Children.Add(outRect);

                        var outLbl = new TextBlock
                        {
                            Text = $"Out{oi + 1}",
                            FontSize = 4.5, Foreground = panelDim,
                            IsHitTestVisible = false,
                            Width = outCellH - 2,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        outLbl.RenderTransform = new System.Windows.Media.RotateTransform(-90);
                        Canvas.SetLeft(outLbl, ocx + outCellW / 2.0 - 3.5);
                        Canvas.SetTop(outLbl,  outY + outCellH - 1);
                        DiagramCanvas.Children.Add(outLbl);
                    }

                    // ── Output lines (one per labeled output, L-shaped: stub down then right) ───────
                    {
                        const double lineLen   = 200.0;  // length of horizontal run
                        const double lineGapY  = 12.0;   // vertical spacing between horizontal lines
                        const double firstGapY =  8.0;   // gap from bottom of output cells to first line
                        const double lineThick =  0.8;
                        const double lblFontSz =  5.5;

                        // Collect labeled output indices, sorted right-to-left so the
                        // rightmost cell gets the topmost (shallowest) line — no stubs
                        // ever cross a horizontal run.
                        var labeledOuts = new System.Collections.Generic.List<int>();
                        for (int oi = 0; oi < outCount; oi++)
                            if (panelCfg.OutputLabels.Count > oi
                                && !string.IsNullOrWhiteSpace(panelCfg.OutputLabels[oi]))
                                labeledOuts.Add(oi);
                        labeledOuts.Sort((a, b) => b.CompareTo(a)); // descending → rightmost first

                        double lineY0 = outY + outCellH + firstGapY;

                        for (int slot = 0; slot < labeledOuts.Count; slot++)
                        {
                            int    oi      = labeledOuts[slot];
                            string userLbl = panelCfg.OutputLabels[oi];
                            double stubX   = outStartX + oi * outCellW + outCellW / 2.0;
                            double ly      = lineY0 + slot * lineGapY;

                            // Vertical stub from cell bottom to the horizontal run
                            PLine(panelStroke, stubX, outY + outCellH, stubX, ly, lineThick);

                            // Horizontal run to the right
                            PLine(panelStroke, stubX, ly, outStartX + lineLen, ly, lineThick);

                            // Label above the horizontal run
                            var lineLbl = new TextBlock
                            {
                                Text             = userLbl,
                                FontSize         = lblFontSz,
                                Foreground       = panelStroke,
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(lineLbl, stubX + 2);
                            Canvas.SetTop(lineLbl,  ly - lblFontSz * 1.35);
                            DiagramCanvas.Children.Add(lineLbl);
                        }
                    }

                    // ── Loop wires (closed rectangular loops) ────────────────────
                    if (loopCount > 0 && panel.LoopInfos.Count > 0)
                    {
                        double slotWire        = leftSecW / loopCount;
                        const double circR     = 3.5;
                        double loopH           = _canvasSettings.WireSpacingPx;
                        const double wireLeft  = 10.0;  // left margin for loop wires
                        var strokeBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                        var circleFill  = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                        var circleStroke= new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

                        // Pass 1: find the majority level (most devices) for each loop
                        var loopMajority = new Dictionary<int, (double Elevation, int TotalDevices, IReadOnlyList<(string, int)> TypeCounts)>();
                        for (int li2 = 0; li2 < panel.LoopInfos.Count; li2++)
                        {
                            var inf = panel.LoopInfos[li2];
                            if (inf.Levels == null || inf.Levels.Count == 0) continue;
                            var majority  = inf.Levels.OrderByDescending(ld => ld.DeviceCount).First();
                            int totalDevs = inf.Levels.Sum(ld => ld.DeviceCount);
                            loopMajority[li2] = (majority.Elevation, totalDevs, majority.TypeCounts);
                        }

                        // Pass 2: per-side stagger maps — key = "elevKey|L" or "elevKey|R"
                        // Loops are bucketed by (majority elevation, side) independently.
                        var sideLevelMap = new Dictionary<string, List<int>>();
                        foreach (var kvp in loopMajority)
                        {
                            var loopInfo2 = panel.LoopInfos[kvp.Key];
                            bool flip2    = _currentVm.IsLoopFlipped(panel.Name, loopInfo2.Name);
                            string side2  = flip2 ? "R" : "L";
                            string sKey   = Math.Round(kvp.Value.Elevation, 3).ToString("F3") + "|" + side2;
                            if (!sideLevelMap.TryGetValue(sKey, out var sl))
                                sideLevelMap[sKey] = sl = new List<int>();
                            sl.Add(kvp.Key);
                        }

                        const double wireRight = 10.0; // right margin (mirror of wireLeft)

                        // Sort each side group by rank (override → natural index fallback),
                        // then build the side-group cache used by the Move Up / Move Down popup.
                        foreach (var sKey in sideLevelMap.Keys.ToList())
                        {
                            sideLevelMap[sKey].Sort((a, b) =>
                            {
                                int rA = _currentVm.GetLoopRank(panel.Name, panel.LoopInfos[a].Name, a);
                                int rB = _currentVm.GetLoopRank(panel.Name, panel.LoopInfos[b].Name, b);
                                return rA.CompareTo(rB);
                            });
                            var orderedKeys = sideLevelMap[sKey]
                                .Select(idx => panel.Name + "::" + panel.LoopInfos[idx].Name)
                                .ToList();
                            foreach (var lk in orderedKeys)
                                _loopSideGroupsCache[lk] = orderedKeys;
                        }

                        // Pass 2.5: pre-compute effective height for every loop so the Y-placement
                        // pass has all heights available without revisiting GetLoopWireCount twice.
                        double wireSpacingGlobal = _canvasSettings.WireSpacingPx;
                        var loopEffH = new Dictionary<int, double>();
                        foreach (var kvp in loopMajority)
                        {
                            int wc2 = _currentVm.GetLoopWireCount(panel.Name, panel.LoopInfos[kvp.Key].Name);
                            loopEffH[kvp.Key] = wireSpacingGlobal * (wc2 - 1);
                        }

                        // Pass 3: draw one closed rectangle per loop, evenly spaced within zone
                        for (int li = 0; li < panel.LoopInfos.Count; li++)
                        {
                            if (!loopMajority.TryGetValue(li, out var maj)) continue;

                            var loopInfo  = panel.LoopInfos[li];
                            string loopKey = panel.Name + "::" + loopInfo.Name;
                            bool flipped  = _currentVm.IsLoopFlipped(panel.Name, loopInfo.Name);
                            bool selected = _currentVm.SelectedLoopKey == loopKey;

                            // Resolve wire color: selected → accent blue; wire color → wire hex; default → dim white
                            Brush wireBrush;
                            if (selected)
                            {
                                wireBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xC3, 0xF7));
                            }
                            else
                            {
                                string wireHex = _currentVm.GetLoopWireColor(panel.Name, loopInfo.Name);
                                if (!string.IsNullOrEmpty(wireHex))
                                {
                                    try
                                    {
                                        var wc = (Color)System.Windows.Media.ColorConverter.ConvertFromString(wireHex);
                                        wireBrush = new SolidColorBrush(Color.FromArgb(0xDD, wc.R, wc.G, wc.B));
                                    }
                                    catch { wireBrush = strokeBrush; }
                                }
                                else
                                {
                                    wireBrush = strokeBrush;
                                }
                            }

                            double laneX     = rectLeft + slotWire * (li + 0.5);
                            string elevKeyStr = Math.Round(maj.Elevation, 3).ToString("F3");
                            string sideKey   = elevKeyStr + "|" + (flipped ? "R" : "L");

                            var closest  = yLookup
                                .OrderBy(e2 => Math.Abs(e2.Elevation - maj.Elevation)).First();
                            double levelY = closest.Y;

                            // Zone ceiling = Y of the next level line above (smaller Y = higher up)
                            double zoneTopY = yLookup
                                .Where(e2 => e2.Y < levelY - 1)
                                .Select(e2 => e2.Y)
                                .DefaultIfEmpty(MarginTop)
                                .Max();

                            // Per-loop wire geometry (height pre-computed in pass 2.5)
                            int    wireCount      = _currentVm.GetLoopWireCount(panel.Name, loopInfo.Name);
                            double wireSpacing    = wireSpacingGlobal;
                            double effectiveLoopH = loopEffH.TryGetValue(li, out var _leh) ? _leh : wireSpacing * (wireCount - 1);

                            var sideList = sideLevelMap.TryGetValue(sideKey, out var sl2)
                                ? sl2 : new List<int>();
                            int n    = Math.Max(sideList.Count, 1);
                            int rank = sideList.IndexOf(li);  // 0 = Loop 1 = bottom
                            if (rank < 0) rank = 0;

                            // Use the tallest loop in the group so effectiveLevelY is consistent
                            // for all loops sharing the same zone.
                            double maxGroupH = sideList.Count > 0
                                ? sideList.Max(idx => loopEffH.TryGetValue(idx, out var _h) ? _h : 0)
                                : effectiveLoopH;

                            // Loops must stay above the panel rect when on its floor level
                            double effectiveLevelY = Math.Min(levelY, rectTop - maxGroupH - 2);
                            double zoneAvail       = effectiveLevelY - zoneTopY;
                            if (zoneAvail < effectiveLoopH + 4) continue; // zone too tight

                            // Gap-based bottom-up placement:
                            // rank 0 → bottommost (Loop 1), rank n-1 → topmost
                            // Equal gap between every pair of adjacent boxes and at both ends.
                            double totalBoxH = sideList.Sum(idx => loopEffH.TryGetValue(idx, out var _lh) ? _lh : 0);
                            double gap = Math.Max(2.0, (zoneAvail - totalBoxH) / (n + 1));

                            // Sum heights of all loops ranked below this one
                            double heightsBelow = 0;
                            for (int r = 0; r < rank; r++)
                                heightsBelow += loopEffH.TryGetValue(sideList[r], out var _lb) ? _lb : 0;

                            double botY = effectiveLevelY - gap * (rank + 1) - heightsBelow;
                            double topY = botY - effectiveLoopH;

                            // ── Compute farEdge — stretch outward if devices overflow ──────────
                            // Use the address-ordered list count as the authoritative total.
                            int    total     = loopInfo.DeviceTypesByAddress.Count;
                            int    maxPerRow = total > 0 ? (int)Math.Ceiling((double)total / wireCount) : 0;
                            double baseEdge  = flipped ? (w - wireRight) : wireLeft;
                            double span0     = Math.Abs(laneX - baseEdge);

                            // Device spacing: fixed setting or auto-fit to default span
                            double deviceSpacing = (_canvasSettings.DeviceSpacingPx > 0 && total > 0)
                                ? _canvasSettings.DeviceSpacingPx
                                : (maxPerRow > 0 ? span0 / (maxPerRow + 1) : span0);

                            // ── Pre-compute per-wire slot lists when repetitions enabled ──────
                            bool showRep = _canvasSettings.ShowRepetitions && total > 0;
                            List<WireSlot>[] wireSlotsByWi = null;
                            int compressedMaxPerRow = maxPerRow;
                            if (showRep)
                            {
                                wireSlotsByWi = new List<WireSlot>[wireCount];
                                int tempRemain = total;
                                for (int wi2 = wireCount - 1; wi2 >= 0; wi2--)
                                {
                                    int wd       = (tempRemain + wi2) / (wi2 + 1);
                                    int startOff = total - tempRemain;
                                    wireSlotsByWi[wi2] = BuildCompressedRow(
                                        loopInfo.DeviceTypesByAddress, startOff, wd);
                                    tempRemain -= wd;
                                }
                                compressedMaxPerRow = wireSlotsByWi.Max(s => s?.Count ?? 0);
                            }

                            // farEdge = one deviceSpacing past the last visible column.
                            double farEdge = baseEdge;
                            if (total > 0)
                            {
                                double requiredReach = deviceSpacing *
                                    ((showRep ? compressedMaxPerRow : maxPerRow) + 1);
                                farEdge = flipped
                                    ? laneX + requiredReach
                                    : laneX - requiredReach;
                            }

                            // ── Vertical spine (panel top → first wire) ───────────
                            Line(wireBrush, laneX, rectTop, laneX, topY);
                            // ── Far vertical (spans full loop height) ─────────────
                            Line(wireBrush, farEdge, topY, farEdge, botY);
                            // ── All horizontal wires (with repetition gaps where needed) ──────
                            double gapHalf = deviceSpacing * 0.44;
                            for (int wi = 0; wi < wireCount; wi++)
                            {
                                double wY2     = topY + wi * wireSpacing;
                                var    slotRow = wireSlotsByWi?[wi];
                                if (slotRow == null || !slotRow.Any(s => s.IsDots))
                                {
                                    Line(wireBrush, farEdge, wY2, laneX, wY2);
                                }
                                else
                                {
                                    // Collect X-range gaps around every dots slot then draw segments
                                    var gaps = new List<(double lo, double hi)>();
                                    for (int si = 0; si < slotRow.Count; si++)
                                    {
                                        if (!slotRow[si].IsDots) continue;
                                        double sx = flipped
                                            ? laneX + deviceSpacing * (si + 1)
                                            : laneX - deviceSpacing * (si + 1);
                                        gaps.Add((sx - gapHalf, sx + gapHalf));
                                    }
                                    gaps.Sort((a, b) => a.lo.CompareTo(b.lo));
                                    double wx0 = Math.Min(laneX, farEdge);
                                    double wx1 = Math.Max(laneX, farEdge);
                                    double cur = wx0;
                                    foreach (var g in gaps)
                                    {
                                        if (g.lo > cur) Line(wireBrush, cur, wY2, g.lo, wY2);
                                        cur = Math.Max(cur, g.hi);
                                    }
                                    if (cur < wx1) Line(wireBrush, cur, wY2, wx1, wY2);
                                }
                            }

                            // ── Hit-test rectangle (full loop height) ─────────────
                            double hitX = Math.Min(laneX, farEdge);
                            double hitW = Math.Abs(laneX - farEdge);
                            var hitRect = new System.Windows.Shapes.Rectangle
                            {
                                Width  = Math.Max(hitW, 8),
                                Height = effectiveLoopH + 4,
                                Fill   = Brushes.Transparent,
                                Tag    = "loop::" + loopKey,
                                Cursor = Cursors.Hand
                            };
                            Canvas.SetLeft(hitRect, hitX);
                            Canvas.SetTop(hitRect,  topY - 2);
                            DiagramCanvas.Children.Add(hitRect);

                            if (total <= 0) continue;

                            // Use the address-ordered device-type list built in DiagramViewModel.
                            // Each entry corresponds to one physical device, sorted address-ascending.
                            var flatTypes = loopInfo.DeviceTypesByAddress;

                            // ── Devices: bottom wire carries the lowest addresses ──────────
                            // wi=wireCount-1 → bottom wire (first in address order)
                            // wi=0           → top wire (last in address order)
                            // di=0           → innermost column
                            // When showRep: dots slots replace compressed runs in-place.
                            int devRemain = total;
                            int devOffset = 0;
                            for (int wi = wireCount - 1; wi >= 0; wi--)
                            {
                                // Ceiling-divide remaining devices among remaining wires (wi+1 left).
                                int wireDevs = (devRemain + wi) / (wi + 1);
                                double wY    = topY + wi * wireSpacing;
                                var slots    = wireSlotsByWi?[wi];
                                int slotCount = slots != null ? slots.Count : wireDevs;

                                for (int di = 0; di < slotCount; di++)
                                {
                                    double devX = flipped
                                        ? laneX + deviceSpacing * (di + 1)
                                        : laneX - deviceSpacing * (di + 1);

                                    if (slots != null && slots[di].IsDots)
                                    {
                                        // ··· repetition marker: 3 small filled dots
                                        double dotR    = 1.5;
                                        double dotStep = gapHalf * 0.65;
                                        for (int d = -1; d <= 1; d++)
                                        {
                                            var dot = new Ellipse
                                            {
                                                Width = dotR * 2, Height = dotR * 2,
                                                Fill  = wireBrush, IsHitTestVisible = false
                                            };
                                            Canvas.SetLeft(dot, devX + d * dotStep - dotR);
                                            Canvas.SetTop(dot,  wY - dotR);
                                            DiagramCanvas.Children.Add(dot);
                                        }
                                        continue;
                                    }

                                    string devType = slots != null
                                        ? slots[di].DeviceType
                                        : (devOffset < flatTypes.Count ? flatTypes[devOffset] : null);
                                    if (slots == null) devOffset++;

                                    // ── Address label (rotated, above wire) ───────────────────────
                                    if (_canvasSettings.ShowAddressLabels)
                                    {
                                        // In slot mode: AddressIndex was recorded in BuildCompressedRow.
                                        // In uncompressed mode: devOffset was already incremented above.
                                        int addrIdx = slots != null ? slots[di].AddressIndex : devOffset - 1;
                                        string devAddr = addrIdx >= 0 && addrIdx < loopInfo.DeviceAddresses.Count
                                            ? loopInfo.DeviceAddresses[addrIdx] : string.Empty;

                                        // Format: zero-pad loop number (strip "Loop " prefix) + "." + address
                                        string shortLoop = loopInfo.Name.StartsWith("Loop ",
                                            StringComparison.OrdinalIgnoreCase)
                                            ? loopInfo.Name.Substring(5).Trim() : loopInfo.Name;
                                        if (int.TryParse(shortLoop, out int ln))
                                            shortLoop = ln.ToString("D2");
                                        if (int.TryParse(devAddr, out int an))
                                            devAddr = an.ToString("D3");
                                        string labelText = shortLoop + "." + devAddr;

                                        // Pill shape: pin Height so the narrow dimension is exact and
                                        // CornerRadius = Height/2 gives perfect rounded ends.
                                        // Width auto-sizes to text (→ visual pill length).
                                        // Measure() provides the actual visual length for SetTop.
                                        const double labelFontSize = 7.0;
                                        const double labelPadH     = 2.0;   // L/R padding
                                        const double labelPadV     = 0.8;   // T/B padding
                                        const double borderThick   = 0.75;
                                        const double labelBorderH  = labelFontSize + labelPadV * 2 + borderThick * 2 + 0.5;
                                        double labelOffset = _canvasSettings.LabelOffsetPx;

                                        var addrLabel = new Border
                                        {
                                            Height           = labelBorderH,
                                            BorderBrush      = wireBrush,
                                            BorderThickness  = new Thickness(borderThick),
                                            CornerRadius     = new CornerRadius(labelBorderH / 2.0),
                                            Background       = Brushes.Transparent,
                                            Padding          = new Thickness(labelPadH, labelPadV, labelPadH, labelPadV),
                                            IsHitTestVisible = false,
                                            LayoutTransform  = new System.Windows.Media.RotateTransform(-90),
                                            Child = new TextBlock
                                            {
                                                Text              = labelText,
                                                FontSize          = labelFontSize,
                                                Foreground        = wireBrush,
                                                VerticalAlignment = VerticalAlignment.Center
                                            }
                                        };

                                        // After -90° LayoutTransform WPF reports DesiredSize in visual space:
                                        //   DesiredSize.Width  = labelBorderH (narrow visual width)  → centre on devX
                                        //   DesiredSize.Height = pill visual length (auto from text) → drives SetTop
                                        addrLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                                        double pillVisualLen = addrLabel.DesiredSize.Height;

                                        double idealTop = wY - circR - labelOffset - pillVisualLen;
                                        double safeTop  = Math.Max(MarginTop - 2.0, idealTop);
                                        Canvas.SetLeft(addrLabel, devX - labelBorderH / 2.0);
                                        Canvas.SetTop(addrLabel,  safeTop);
                                        DiagramCanvas.Children.Add(addrLabel);
                                    }

                                    string slotSymKey = devType != null
                                        ? _currentVm.GetDeviceTypeSymbol(devType) : null;
                                    CustomSymbolDefinition slotSym = slotSymKey != null && _symbolLibrary != null
                                        ? _symbolLibrary.FirstOrDefault(s =>
                                            string.Equals(s.Name, slotSymKey, StringComparison.OrdinalIgnoreCase))
                                        : null;

                                    if (slotSym != null)
                                    {
                                        AddSymbolToCanvas(devX, wY, circR * 2.8, slotSym, circleStroke);
                                    }
                                    else
                                    {
                                        var circle = new Ellipse
                                        {
                                            Width = circR * 2, Height = circR * 2,
                                            Stroke = circleStroke, StrokeThickness = 1,
                                            Fill = circleFill, IsHitTestVisible = false
                                        };
                                        Canvas.SetLeft(circle, devX - circR);
                                        Canvas.SetTop(circle,  wY  - circR);
                                        DiagramCanvas.Children.Add(circle);
                                    }
                                }
                                devRemain -= wireDevs;
                            }
                        }

                        // Local helper to add a line without repeating boilerplate
                        void Line(Brush stroke, double x1, double y1, double x2, double y2) =>
                            DiagramCanvas.Children.Add(new Line
                            {
                                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                                Stroke = stroke, StrokeThickness = 1, IsHitTestVisible = false
                            });
                    }
                }
            }

            UpdateRestoreButton();
        }

        // ── Loop popup ────────────────────────────────────────────────────

        private string _popupLoopKey;      // "panelName::loopName" open in LoopPopup
        private string _moveUpTargetKey;   // loopKey one rank above the popup's loop (null if none)
        private string _moveDownTargetKey; // loopKey one rank below the popup's loop (null if none)
        private int    _popupNaturalIdx;
        private int    _moveUpNaturalIdx;
        private int    _moveDownNaturalIdx;
        private bool   _suppressWireChange;

        // Side-group cache rebuilt each DrawLevels: loopKey → sorted list of loopKeys in same group
        private readonly Dictionary<string, List<string>> _loopSideGroupsCache
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private void LoopPopupFlip_Click(object sender, RoutedEventArgs e)
        {
            LoopPopup.IsOpen = false;
            _currentVm?.FlipSelectedLoop();
        }

        private void LoopPopupAddLine_Click(object sender, RoutedEventArgs e)
        {
            _currentVm?.AddLineToSelectedLoop(); // act before close so SelectedLoopKey is still set
            LoopPopup.IsOpen = false;
        }

        private void LoopPopupRemoveLine_Click(object sender, RoutedEventArgs e)
        {
            _currentVm?.RemoveLineFromSelectedLoop();
            LoopPopup.IsOpen = false;
        }

        private void LoopPopupMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVm == null || _moveUpTargetKey == null) return;
            _currentVm.SwapLoopRanks(_popupLoopKey, _moveUpTargetKey, _popupNaturalIdx, _moveUpNaturalIdx);
            LoopPopup.IsOpen = false;
        }

        private void LoopPopupMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVm == null || _moveDownTargetKey == null) return;
            _currentVm.SwapLoopRanks(_popupLoopKey, _moveDownTargetKey, _popupNaturalIdx, _moveDownNaturalIdx);
            LoopPopup.IsOpen = false;
        }

        private void LoopPopupWireCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressWireChange) return;
            if (!LoopPopup.IsOpen || _currentVm == null || string.IsNullOrEmpty(_popupLoopKey)) return;

            var selected = LoopPopupWireCombo.SelectedItem as string;
            int dbl2 = _popupLoopKey.IndexOf("::", StringComparison.Ordinal);
            string pName = dbl2 >= 0 ? _popupLoopKey.Substring(0, dbl2) : _popupLoopKey;
            string lName = dbl2 >= 0 ? _popupLoopKey.Substring(dbl2 + 2) : _popupLoopKey;

            string wireName = (selected == null || selected == "(None)") ? null : selected;
            _currentVm.SetLoopWire(pName, lName, wireName);
        }

        // ── Level context popup ───────────────────────────────────────────

        private void DiagramContent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_inMoveMode)
            {
                ExitMoveMode(commit: true);
                e.Handled = true;
            }
        }

        private void DiagramContent_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                DiagramContent.ReleaseMouseCapture();
                DiagramContent.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void DiagramCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Commit move mode on any left-click
            if (_inMoveMode)
            {
                ExitMoveMode(commit: true);
                e.Handled = true;
                return;
            }

            if (_currentVm == null) return;

            var tag = (e.OriginalSource as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            // ── Loop wire click → show loop popup ─────────────────────────
            if (tag.StartsWith("loop::", StringComparison.Ordinal))
            {
                _popupLoopKey = tag.Substring(6); // "panelName::loopName"
                _currentVm.SelectedLoopKey = _popupLoopKey;
                DrawLevels();

                int dbl2      = _popupLoopKey.IndexOf("::", StringComparison.Ordinal);
                string pName  = dbl2 >= 0 ? _popupLoopKey.Substring(0, dbl2)  : _popupLoopKey;
                string lName  = dbl2 >= 0 ? _popupLoopKey.Substring(dbl2 + 2) : _popupLoopKey;
                LoopPopupTitle.Text = lName;

                bool isFlipped = _currentVm.IsLoopFlipped(pName, lName);
                LoopPopupFlipButton.Content = MakeButtonContent(
                    PackIconKind.SwapHorizontal,
                    isFlipped ? "Flip to left side" : "Flip to right side");

                int wires = _currentVm.GetLoopWireCount(pName, lName);
                LoopPopupAddLineButton.Content = MakeButtonContent(
                    PackIconKind.Plus,
                    $"Add line ({wires} \u2192 {wires + 1})");
                LoopPopupAddLineButton.IsEnabled = wires < 8;
                LoopPopupRemoveLineButton.Content = MakeButtonContent(
                    PackIconKind.Minus,
                    $"Remove line ({wires} \u2192 {wires - 1})");
                LoopPopupRemoveLineButton.Visibility = wires > 2
                    ? Visibility.Visible : Visibility.Collapsed;

                // Move Up / Move Down — look up the sorted side group built by DrawLevels
                _moveUpTargetKey   = null;
                _moveDownTargetKey = null;
                _popupNaturalIdx   = 0;
                _moveUpNaturalIdx  = 0;
                _moveDownNaturalIdx = 0;

                if (_loopSideGroupsCache.TryGetValue(_popupLoopKey, out var group))
                {
                    var panel2    = _currentVm.Panels.FirstOrDefault(p => p.Name == pName);
                    int myPosInGrp = group.IndexOf(_popupLoopKey);
                    _popupNaturalIdx = panel2.LoopInfos != null
                        ? panel2.LoopInfos.ToList().FindIndex(l => l.Name == lName)
                        : 0;

                    if (myPosInGrp > 0) // there is a loop below
                    {
                        _moveDownTargetKey = group[myPosInGrp - 1];
                        string dnName = _moveDownTargetKey.Contains("::") ? _moveDownTargetKey.Substring(_moveDownTargetKey.IndexOf("::")+2) : _moveDownTargetKey;
                        _moveDownNaturalIdx = panel2.LoopInfos != null
                            ? panel2.LoopInfos.ToList().FindIndex(l => l.Name == dnName)
                            : 0;
                    }
                    if (myPosInGrp < group.Count - 1) // there is a loop above
                    {
                        _moveUpTargetKey = group[myPosInGrp + 1];
                        string upName = _moveUpTargetKey.Contains("::") ? _moveUpTargetKey.Substring(_moveUpTargetKey.IndexOf("::")+2) : _moveUpTargetKey;
                        _moveUpNaturalIdx = panel2.LoopInfos != null
                            ? panel2.LoopInfos.ToList().FindIndex(l => l.Name == upName)
                            : 0;
                    }
                }

                LoopPopupMoveUpButton.IsEnabled   = _moveUpTargetKey   != null;
                LoopPopupMoveDownButton.IsEnabled = _moveDownTargetKey != null;
                LoopPopupMoveUpButton.Visibility   = Visibility.Visible;
                LoopPopupMoveDownButton.Visibility = Visibility.Visible;

                // Populate wire combobox
                _suppressWireChange = true;
                var wireNames = new System.Collections.Generic.List<string> { "(None)" };
                wireNames.AddRange(_currentVm.GetAvailableWireNames());
                LoopPopupWireCombo.ItemsSource = wireNames;
                string currentWire = _currentVm.GetLoopWire(pName, lName);
                LoopPopupWireCombo.SelectedItem = string.IsNullOrEmpty(currentWire) ? "(None)" : currentWire;
                LoopPopupWireCombo.Visibility = wireNames.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                _suppressWireChange = false;

                LoopPopup.IsOpen = true;
                e.Handled = true;
                return;
            }

            // ── Level popup (existing) ────────────────────────────────────
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

            // Move button only makes sense for line elements
            PopupMoveButton.Visibility = kind == "line" ? Visibility.Visible : Visibility.Collapsed;
            if (kind == "line")
                PopupMoveButton.Content = MakeButtonContent(PackIconKind.ArrowUpDown, "Move");

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

        // ── Per-panel diagram settings helpers ──────────────────────────────

        private string _popupPanelName;    // panel name currently open in PanelPopup
        private string _selectedPanelName; // panel whose border is highlighted

        private PanelDiagramSettings GetPanelCfg(string panelName)
        {
            if (_canvasSettings.PanelSettings.TryGetValue(panelName, out var cfg))
                return cfg;
            return new PanelDiagramSettings();
        }

        private void ShowPanelEditPopup(string panelName, double canvasX, double canvasY)
        {
            _popupPanelName = panelName;
            var cfg = GetPanelCfg(panelName);

            PanelPopupTitle.Text    = panelName;
            PanelPopupName.Text     = cfg.Name.Replace("\n", System.Environment.NewLine);
            PanelPopupOutCount.Text = cfg.OutCount.ToString();
            PanelPopupSupply.Text   = cfg.Supply;

            RefreshOutputsPanel(cfg.OutCount, cfg.OutputLabels);

            PanelPopup.IsOpen = true;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => PanelPopupName.Focus()));
        }

        /// <summary>Rebuilds the per-output label text-boxes inside the popup.</summary>
        private void RefreshOutputsPanel(int count, System.Collections.Generic.List<string> existingLabels = null)
        {
            PanelPopupOutputsPanel.Children.Clear();
            for (int i = 0; i < count; i++)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin      = new Thickness(0, 0, 0, 4)
                };

                var lbl = new TextBlock
                {
                    Text              = $"Out {i + 1}",
                    FontSize          = 10,
                    Width             = 44,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity           = 0.60,
                    Foreground        = (Brush)TryFindResource("ForegroundBrush")
                                        ?? (Brush)Brushes.White
                };

                var tb = new TextBox
                {
                    Text             = (existingLabels != null && existingLabels.Count > i)
                                       ? existingLabels[i] : string.Empty,
                    FontSize         = 11,
                    Width            = 160,
                    Padding          = new Thickness(6, 3, 6, 3),
                    Foreground       = (Brush)TryFindResource("ForegroundBrush")
                                       ?? (Brush)Brushes.White,
                    Background       = (Brush)TryFindResource("BackgroundBrush")
                                       ?? (Brush)new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)),
                    BorderBrush      = (Brush)TryFindResource("BorderBrush")
                                       ?? (Brush)new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    CaretBrush       = (Brush)TryFindResource("ForegroundBrush")
                                       ?? (Brush)Brushes.White,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                row.Children.Add(lbl);
                row.Children.Add(tb);
                PanelPopupOutputsPanel.Children.Add(row);
            }
        }

        private void PanelPopupOutDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PanelPopupOutCount.Text, out int v) && v > 0)
            {
                PanelPopupOutCount.Text = (v - 1).ToString();
                RefreshOutputsPanel(v - 1, CurrentOutputLabels());
            }
        }

        private void PanelPopupOutUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PanelPopupOutCount.Text, out int v))
            {
                PanelPopupOutCount.Text = (v + 1).ToString();
                RefreshOutputsPanel(v + 1, CurrentOutputLabels());
            }
        }

        /// <summary>Reads current output label values from the dynamic popup panel.</summary>
        private System.Collections.Generic.List<string> CurrentOutputLabels()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (FrameworkElement row in PanelPopupOutputsPanel.Children)
            {
                if (row is StackPanel sp)
                    foreach (UIElement child in sp.Children)
                        if (child is TextBox tb)
                            list.Add(tb.Text.Trim());
            }
            return list;
        }

        private void PanelPopupSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_popupPanelName)) return;

            string newName = PanelPopupName.Text
                .Replace(System.Environment.NewLine, "\n").TrimEnd();
            string supply = PanelPopupSupply.Text.Trim();
            if (!int.TryParse(PanelPopupOutCount.Text.Trim(), out int newOut) || newOut < 0)
                newOut = GetPanelCfg(_popupPanelName).OutCount;

            _canvasSettings.PanelSettings[_popupPanelName] = new PanelDiagramSettings
            {
                Name         = newName,
                OutCount     = newOut,
                Supply       = supply,
                OutputLabels = CurrentOutputLabels()
            };
            DiagramCanvasSettingsService.Save(_canvasSettings);
            PanelPopup.IsOpen = false;
            DrawLevels();
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

        private void PopupMove_Click(object sender, RoutedEventArgs e)
        {
            LevelPopup.IsOpen = false;
            if (_currentVm == null || _popupTargetLevel == null) return;

            var lev = _currentVm.Levels.FirstOrDefault(l => l.Name == _popupTargetLevel);
            if (lev == null) return;

            _movingLevel      = lev;
            _moveOriginalElev = lev.Elevation;
            _inMoveMode       = true;
            DiagramContent.Cursor = Cursors.SizeNS;
            DiagramContent.CaptureMouse();
            DiagramContent.Focus();
            DrawLevels();
        }

        private void DiagramContent_MouseMove(object sender, MouseEventArgs e)
        {
            // ── Middle-mouse pan ─────────────────────────────────────────
            if (_isPanning)
            {
                Point cur = e.GetPosition(DiagramContent);
                _zoomTT.X = _panStartX + (cur.X - _panStart.X);
                _zoomTT.Y = _panStartY + (cur.Y - _panStart.Y);
                e.Handled = true;
                return;
            }

            if (!_inMoveMode || _movingLevel == null || _drawRange < 0.001) return;

            // Convert mouse position (DiagramContent space) → canvas space (undo zoom transform)
            Point mouse   = e.GetPosition(DiagramContent);
            double canvasY = (mouse.Y - _zoomTT.Y) / _zoomST.ScaleY;

            // Invert the draw formula:  y = MarginTop + (1 - (elev-min)/range) * drawH
            double t       = 1.0 - (canvasY - MarginTop) / _drawDrawH;
            double newElev = _drawMinElev + t * _drawRange;

            // Clamp between immediate neighbours so the line can't jump past them
            var others = _currentVm.Levels
                .Where(l => l != _movingLevel
                         && _currentVm.GetLineState(l.Name) != LevelState.Deleted)
                .OrderBy(l => l.Elevation)
                .ToList();

            const double gap = 0.0001;
            double lo = others.Where(l => l.Elevation <= _moveOriginalElev - gap)
                               .Select(l => l.Elevation)
                               .DefaultIfEmpty(double.MinValue).Max();
            double hi = others.Where(l => l.Elevation >= _moveOriginalElev + gap)
                               .Select(l => l.Elevation)
                               .DefaultIfEmpty(double.MaxValue).Min();

            if (lo > double.MinValue) newElev = Math.Max(lo + gap, newElev);
            if (hi < double.MaxValue) newElev = Math.Min(hi - gap, newElev);

            if (Math.Abs(newElev - _movingLevel.Elevation) < 1e-9) return;
            _movingLevel.Elevation = newElev;
            DrawLevels();
        }

        private void DiagramContent_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_inMoveMode) return;
            if (e.Key == Key.Escape)
            {
                ExitMoveMode(commit: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                ExitMoveMode(commit: true);
                e.Handled = true;
            }
        }

        private void ExitMoveMode(bool commit)
        {
            if (!_inMoveMode) return;
            _inMoveMode = false;
            if (!commit && _movingLevel != null)
                _movingLevel.Elevation = _moveOriginalElev;
            else if (commit && _movingLevel != null)
                _currentVm?.PersistLevelElevationOffset(_movingLevel.Name, _movingLevel.Elevation);
            _movingLevel = null;
            DiagramContent.ReleaseMouseCapture();
            DiagramContent.Cursor = Cursors.Arrow;
            DrawLevels();
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

        // ── Canvas settings popup ───────────────────────────────────────

        private void CanvasSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Populate textboxes with current values
            TbWireSpacing.Text    = _canvasSettings.WireSpacingPx.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture);
            TbDeviceSpacing.Text  = _canvasSettings.DeviceSpacingPx.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture);
            CbShowRepetitions.IsChecked   = _canvasSettings.ShowRepetitions;
            CbShowAddressLabels.IsChecked  = _canvasSettings.ShowAddressLabels;
            TbLabelOffset.Text             = _canvasSettings.LabelOffsetPx.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture);
            CanvasSettingsPopup.IsOpen = true;
        }

        private void BtnCanvasSettingsApply_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TbWireSpacing.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double ws) && ws >= 4)
                _canvasSettings.WireSpacingPx = ws;

            if (double.TryParse(TbDeviceSpacing.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double ds) && ds >= 0)
                _canvasSettings.DeviceSpacingPx = ds;

            _canvasSettings.ShowRepetitions   = CbShowRepetitions.IsChecked   == true;
            _canvasSettings.ShowAddressLabels  = CbShowAddressLabels.IsChecked == true;

            if (double.TryParse(TbLabelOffset.Text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double lo) && lo >= 0)
                _canvasSettings.LabelOffsetPx = lo;

            DiagramCanvasSettingsService.Save(_canvasSettings);
            CanvasSettingsPopup.IsOpen = false;
            DrawLevels();
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

        // ── Custom symbol rendering ───────────────────────────────────────

        /// <summary>
        /// Renders a <see cref="CustomSymbolDefinition"/> on the diagram canvas so that the
        /// symbol's snap origin is aligned to (cx, cy).  The symbol is scaled so the larger
        /// of its two viewbox dimensions equals <paramref name="sizePx"/>.
        /// Falls back silently if the symbol has no elements.
        /// </summary>
        private void AddSymbolToCanvas(double cx, double cy, double sizePx,
                                        CustomSymbolDefinition symbol, Brush defaultStroke)
        {
            if (symbol?.Elements == null || symbol.Elements.Count == 0) return;

            // Use a fixed 10mm reference so that all symbols render at the same px/mm
            // ratio regardless of their viewbox size.  sizePx therefore means:
            // "how many canvas pixels should 10mm of symbol content occupy".
            // (Previously this divided by Math.Max(ViewboxWidthMm, ViewboxHeightMm) which
            // caused symbols with different viewbox dimensions to appear at inconsistent scales.)
            const double SymbolRefMm = 10.0;
            double scale = sizePx / SymbolRefMm;  // px per mm

            // Align the snap origin to the device centre position
            double ox = cx - symbol.SnapOriginXMm * scale;
            double oy = cy - symbol.SnapOriginYMm * scale;

            foreach (var el in symbol.Elements)
            {
                var shape = CreateDiagramShape(el, scale, ox, oy, defaultStroke);
                if (shape == null) continue;
                DiagramCanvas.Children.Add(shape);
            }
        }

        private static UIElement CreateDiagramShape(SymbolElement el, double scale,
                                                     double ox, double oy, Brush defaultStroke)
        {
            var stroke = TryParseBrush(el.StrokeColor) ?? defaultStroke;
            var fill   = el.IsFilled ? (TryParseBrush(el.FillColor) ?? Brushes.Transparent) : Brushes.Transparent;
            double thick = Math.Max(0.6, el.StrokeThicknessMm * scale);

            switch (el.Type)
            {
                case SymbolElementType.Line:
                {
                    if (el.Points.Count < 2) return null;
                    return new Line
                    {
                        X1 = ox + el.Points[0].X * scale, Y1 = oy + el.Points[0].Y * scale,
                        X2 = ox + el.Points[1].X * scale, Y2 = oy + el.Points[1].Y * scale,
                        Stroke = stroke, StrokeThickness = thick, IsHitTestVisible = false
                    };
                }
                case SymbolElementType.Polyline:
                {
                    if (el.Points.Count < 2) return null;
                    var pts = new PointCollection(
                        el.Points.Select(p => new Point(ox + p.X * scale, oy + p.Y * scale)));
                    if (el.IsClosed || el.IsFilled)
                        return new Polygon  { Points = pts, Stroke = stroke, StrokeThickness = thick, Fill = fill, IsHitTestVisible = false };
                    return new Polyline { Points = pts, Stroke = stroke, StrokeThickness = thick, IsHitTestVisible = false };
                }
                case SymbolElementType.Circle:
                {
                    if (el.Points.Count < 2) return null;
                    double cx2 = ox + el.Points[0].X * scale, cy2 = oy + el.Points[0].Y * scale;
                    double r = Math.Sqrt(
                        Math.Pow((ox + el.Points[1].X * scale) - cx2, 2) +
                        Math.Pow((oy + el.Points[1].Y * scale) - cy2, 2));
                    if (r < 0.3) return null;
                    var e2 = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = thick, Fill = fill, IsHitTestVisible = false };
                    Canvas.SetLeft(e2, cx2 - r);
                    Canvas.SetTop(e2,  cy2 - r);
                    return e2;
                }
                case SymbolElementType.Rectangle:
                {
                    if (el.Points.Count < 2) return null;
                    double rx = Math.Min(el.Points[0].X, el.Points[1].X) * scale + ox;
                    double ry = Math.Min(el.Points[0].Y, el.Points[1].Y) * scale + oy;
                    double rw = Math.Abs(el.Points[1].X - el.Points[0].X) * scale;
                    double rh = Math.Abs(el.Points[1].Y - el.Points[0].Y) * scale;
                    if (rw < 0.5 || rh < 0.5) return null;
                    var rect = new System.Windows.Shapes.Rectangle { Width = rw, Height = rh, Stroke = stroke, StrokeThickness = thick, Fill = fill, IsHitTestVisible = false };
                    Canvas.SetLeft(rect, rx);
                    Canvas.SetTop(rect,  ry);
                    return rect;
                }
            }
            return null;
        }

        private static Brush TryParseBrush(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(c);
            }
            catch { return null; }
        }
    }
}
