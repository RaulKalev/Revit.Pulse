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
                            X2              = w - 4,
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
                        X2               = w - 4,
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
                    double rectTop  = zoneBottom - 10.0 - rectH;  // 10 px above floor level line

                    // Left section (loops + body) and right section (power/battery)
                    const double rightSecW = 52.0;
                    double leftSecW        = rectW - rightSecW;

                    // Common brushes
                    var panelStroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
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

                    // ── Loop output header (left section only) ───────────
                    int loopCount = panel.ConfigLoopCount > 0
                        ? panel.ConfigLoopCount
                        : panel.LoopInfos.Count;
                    loopCount = Math.Min(loopCount, 16);

                    const double headerH   = 52.0;
                    const double lblFont   = 7.0;
                    double approxLineH     = lblFont * 1.55;

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

                    // ---- Battery symbol (centered in header zone of right section) ----
                    // Standard IEC battery: alternating long-thin (−) and short-thick (+) plates
                    // connected by a central vertical conductor, two cells shown.
                    double batAreaCY = rectTop + headerH / 2.0;  // vertical centre of header zone
                    double batHalf   = 22.0;                      // half total symbol height
                    double batSY     = batAreaCY - batHalf;       // symbol top
                    double plateW_thin  = 16.0;
                    double plateW_thick = 10.0;

                    // Central vertical conductor (full height of symbol)
                    PLine(panelDim, rsCX, batSY, rsCX, batSY + batHalf * 2);

                    // Cell 1  (upper)
                    // — thin plate (−)
                    PLine(panelDim,    rsCX - plateW_thin / 2,  batSY + 6,  rsCX + plateW_thin / 2,  batSY + 6,  1.0);
                    // — thick plate (+)
                    PLine(panelStroke, rsCX - plateW_thick / 2, batSY + 11, rsCX + plateW_thick / 2, batSY + 11, 2.5);

                    // Cell 2  (lower)
                    // — thin plate (−)
                    PLine(panelDim,    rsCX - plateW_thin / 2,  batSY + 26, rsCX + plateW_thin / 2,  batSY + 26, 1.0);
                    // — thick plate (+)
                    PLine(panelStroke, rsCX - plateW_thick / 2, batSY + 31, rsCX + plateW_thick / 2, batSY + 31, 2.5);

                    // Dashed vertical gap between the two cells (polarity indicator)
                    var batDash = new Line
                    {
                        X1 = rsCX, Y1 = batSY + 14, X2 = rsCX, Y2 = batSY + 23,
                        Stroke = panelDimmer, StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        IsHitTestVisible = false
                    };
                    DiagramCanvas.Children.Add(batDash);

                    // ---- Horizontal divider inside right section (header / body boundary) ----
                    double rsDivY = rectTop + headerH;
                    PLine(panelDim, rsLeft + 1, rsDivY, rsLeft + rightSecW - 1, rsDivY);

                    // ---- Diagonal PSU section (body zone of right section) ----
                    double diagTop    = rsDivY + 1;
                    double diagBottom = rectTop + rectH - 2;
                    double diagBoxH   = diagBottom - diagTop;
                    double diagBoxL   = rsLeft + 2;
                    double diagBoxR   = rsLeft + rightSecW - 2;

                    // No inner border rect — the outer panel rect + divider already frame it.
                    // Single diagonal: bottom-left → top-right
                    PLine(panelDimmer, diagBoxL, diagBottom, diagBoxR, diagTop);

                    // Rod (fuse/indicator) symbol — upper-right area, short vertical bar with caps
                    double rodX  = diagBoxL + (diagBoxR - diagBoxL) * 0.68;
                    double rodMY = diagTop  + diagBoxH * 0.32;
                    PLine(panelStroke, rodX, rodMY - 7, rodX, rodMY + 7);          // main rod
                    PLine(panelDim,    rodX - 3, rodMY - 7, rodX + 3, rodMY - 7); // top cap
                    PLine(panelDim,    rodX - 3, rodMY + 7, rodX + 3, rodMY + 7); // bottom cap

                    // Coil/cord symbol — lower-left area, open arc (backward-C)
                    double coilCX = diagBoxL + (diagBoxR - diagBoxL) * 0.32;
                    double coilCY = diagTop  + diagBoxH * 0.70;
                    double coilR  = 5.5;
                    var coilPath = new System.Windows.Shapes.Path
                    {
                        Stroke = panelDim, StrokeThickness = 1,
                        Fill = Brushes.Transparent, IsHitTestVisible = false,
                        Data = new PathGeometry(new[]
                        {
                            new PathFigure(
                                new Point(coilCX, coilCY - coilR),
                                new PathSegment[]
                                {
                                    new ArcSegment(
                                        new Point(coilCX, coilCY + coilR),
                                        new System.Windows.Size(coilR, coilR),
                                        0, true,
                                        SweepDirection.Counterclockwise, true)
                                },
                                false)
                        })
                    };
                    DiagramCanvas.Children.Add(coilPath);
                    // Short tail below coil
                    PLine(panelDim, coilCX, coilCY + coilR, coilCX, coilCY + coilR + 4);

                    // ── Power connection: from right panel edge → ground symbol + label ──
                    double pwrY  = rsDivY + diagBoxH * 0.45;  // midway in diagonal section
                    double gndX0 = rectLeft + rectW;           // panel right edge
                    double gndX1 = gndX0 + 14;                 // start of ground symbol
                    PLine(panelDim, gndX0, pwrY, gndX1, pwrY);
                    // 3-bar ground symbol (bars centred on gndX1)
                    PLine(panelStroke, gndX1 - 6, pwrY,     gndX1 + 6, pwrY);
                    PLine(panelStroke, gndX1 - 4, pwrY + 3, gndX1 + 4, pwrY + 3);
                    PLine(panelStroke, gndX1 - 2, pwrY + 6, gndX1 + 2, pwrY + 6);
                    // Label below ground symbol
                    var pwrLabel = new TextBlock
                    {
                        Text = "Toide 230V", FontSize = 6,
                        Foreground = panelDim, IsHitTestVisible = false
                    };
                    Canvas.SetLeft(pwrLabel, gndX1 - 10);
                    Canvas.SetTop(pwrLabel,  pwrY + 10);
                    DiagramCanvas.Children.Add(pwrLabel);

                    // ── Body: subtitle lines + panel name (left section, below header) ──
                    double bodyTop = (loopCount > 0 && rectH > headerH + 12)
                        ? rectTop + headerH + 2
                        : rectTop;
                    double bodyH = (rectTop + rectH) - bodyTop;

                    // Sub-title 1
                    var sub1 = new TextBlock
                    {
                        Text = "Tulekahjusignalisatsiooni keskseade",
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
                        Text = "Analoogadresseeritav",
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
                    const int    outCount  = 5;
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

                            // Loops must stay above the panel rect when on its floor level
                            double effectiveLevelY = Math.Min(levelY, rectTop - loopH - 2);
                            double zoneAvail       = effectiveLevelY - zoneTopY;
                            if (zoneAvail < loopH + 4) continue; // zone too tight

                            var sideList = sideLevelMap.TryGetValue(sideKey, out var sl2)
                                ? sl2 : new List<int>();
                            int n    = Math.Max(sideList.Count, 1);
                            int rank = sideList.IndexOf(li);
                            if (rank < 0) rank = 0;

                            // Even spacing: distribute loops from effectiveLevelY upward
                            double pitch = zoneAvail / (n + 1);
                            double topY  = effectiveLevelY - pitch * (rank + 1);

                            // Number of horizontal wires (default 2 = top + bottom)
                            int    wireCount      = _currentVm.GetLoopWireCount(panel.Name, loopInfo.Name);
                            double wireSpacing    = _canvasSettings.WireSpacingPx;
                            double effectiveLoopH = wireSpacing * (wireCount - 1);
                            double botY = topY + effectiveLoopH;

                            // ── Compute farEdge — stretch outward if devices overflow ──────────
                            int    total     = maj.TotalDevices;
                            int    maxPerRow = total > 0 ? (int)Math.Ceiling((double)total / wireCount) : 0;
                            double baseEdge  = flipped ? (w - wireRight) : wireLeft;
                            double span0     = Math.Abs(laneX - baseEdge);

                            // Device spacing: fixed setting or auto-fit to default span
                            double deviceSpacing = (_canvasSettings.DeviceSpacingPx > 0 && total > 0)
                                ? _canvasSettings.DeviceSpacingPx
                                : (maxPerRow > 0 ? span0 / (maxPerRow + 1) : span0);

                            // If fixed spacing causes overflow, push farEdge outward
                            double farEdge = baseEdge;
                            if (total > 0)
                            {
                                double requiredReach = deviceSpacing * (maxPerRow + 1);
                                farEdge = flipped
                                    ? Math.Max(baseEdge, laneX + requiredReach)
                                    : Math.Min(baseEdge, laneX - requiredReach);
                            }

                            // ── Vertical spine (panel top → first wire) ───────────
                            Line(wireBrush, laneX, rectTop, laneX, topY);
                            // ── Far vertical (spans full loop height) ─────────────
                            Line(wireBrush, farEdge, topY, farEdge, botY);
                            // ── All horizontal wires ──────────────────────────────
                            for (int wi = 0; wi < wireCount; wi++)
                                Line(wireBrush, farEdge, topY + wi * wireSpacing, laneX, topY + wi * wireSpacing);

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

                            // Build a flat, ordered list of device types for this loop
                            var flatTypes = new List<string>(total);
                            if (maj.TypeCounts != null)
                            {
                                foreach (var (dt, cnt) in maj.TypeCounts)
                                    for (int k = 0; k < cnt; k++)
                                        flatTypes.Add(dt);
                            }
                            else
                            {
                                for (int k = 0; k < total; k++) flatTypes.Add(null);
                            }

                            // ── Devices: each wire row starts at laneX and grows outward ─────
                            // Right-side (flipped):  laneX + ds*(di+1)  → grows rightward
                            // Left-side  (normal):   laneX - ds*(di+1)  → grows leftward
                            // Using per-row column index di (not a global offset) means column 0
                            // lands at the same X on every wire row → true vertical alignment.
                            int devRemain = total;
                            int devOffset = 0;    // tracks position in flatTypes for device-type labels
                            for (int wi = 0; wi < wireCount; wi++)
                            {
                                // Ceiling-divide remaining devices among remaining wires
                                int wireDevs = (devRemain + (wireCount - wi) - 1) / (wireCount - wi);
                                double wY    = topY + wi * wireSpacing;
                                for (int di = 0; di < wireDevs; di++)
                                {
                                    // di is the per-row column index → same column = same X across rows
                                    double devX = flipped
                                        ? laneX + deviceSpacing * (di + 1)
                                        : laneX - deviceSpacing * (di + 1);
                                    string devType = devOffset < flatTypes.Count ? flatTypes[devOffset] : null;
                                    devOffset++;

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

        private string _popupLoopKey; // "panelName::loopName" open in LoopPopup
        private bool   _suppressWireChange;

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
