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

                    // ---- Battery symbol (aligns with header area) ----
                    double vl_x  = rsCX - 6;
                    double vr_x  = rsCX + 6;
                    double batY  = rectTop + 8;

                    // Top cross-bar
                    PLine(panelDim, vl_x, batY, vr_x, batY);
                    // Conductor: top → cell 1
                    PLine(panelDim, vl_x, batY,      vl_x, batY + 9);
                    PLine(panelDim, vr_x, batY,      vr_x, batY + 9);
                    // Cell 1 — thick plate (short, 2px) + thin plate (wider, 1px)
                    PLine(panelStroke, vl_x - 1, batY + 9,  vr_x + 1, batY + 9,  2.0);
                    PLine(panelDim,    vl_x - 4, batY + 13, vr_x + 4, batY + 13, 1.0);
                    // Conductor between cells
                    PLine(panelDim, vl_x, batY + 13, vl_x, batY + 21);
                    PLine(panelDim, vr_x, batY + 13, vr_x, batY + 21);
                    // Cell 2
                    PLine(panelStroke, vl_x - 1, batY + 21, vr_x + 1, batY + 21, 2.0);
                    PLine(panelDim,    vl_x - 4, batY + 25, vr_x + 4, batY + 25, 1.0);
                    // Conductor: cell 2 → bottom cross-bar
                    PLine(panelDim, vl_x, batY + 25, vl_x, batY + 34);
                    PLine(panelDim, vr_x, batY + 25, vr_x, batY + 34);
                    // Bottom cross-bar
                    PLine(panelDim, vl_x, batY + 34, vr_x, batY + 34);

                    // ---- Diagonal power supply section (aligns with body area) ----
                    double diagTop    = rectTop + headerH + 2;
                    double diagBottom = rectTop + rectH - 3;
                    double diagBoxH   = diagBottom - diagTop;

                    var diagRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rightSecW - 4, Height = diagBoxH,
                        Stroke = panelDim, StrokeThickness = 1,
                        Fill = Brushes.Transparent, IsHitTestVisible = false
                    };
                    Canvas.SetLeft(diagRect, rsLeft + 2);
                    Canvas.SetTop(diagRect,  diagTop);
                    DiagramCanvas.Children.Add(diagRect);

                    // Diagonal lines (X pattern = transformer/PSU symbol)
                    PLine(panelDimmer, rsLeft + 2,              diagBottom, rsLeft + rightSecW - 2, diagTop);
                    PLine(panelDimmer, rsLeft + 2,              diagTop,    rsLeft + rightSecW - 2, diagBottom);

                    // Plug/socket symbol (small circle + stem) in lower-left quadrant
                    double plugCX = rsLeft + rightSecW * 0.32;
                    double plugCY = diagTop + diagBoxH * 0.62;
                    double plugR  = 5.0;
                    var plugCirc = new Ellipse
                    {
                        Width = plugR * 2, Height = plugR * 2,
                        Stroke = panelDim, StrokeThickness = 1,
                        Fill = Brushes.Transparent, IsHitTestVisible = false
                    };
                    Canvas.SetLeft(plugCirc, plugCX - plugR);
                    Canvas.SetTop(plugCirc,  plugCY - plugR);
                    DiagramCanvas.Children.Add(plugCirc);
                    PLine(panelDim, plugCX, plugCY + plugR, plugCX, plugCY + plugR + 5);

                    // ── Power connection: horizontal run + ground + label (outside panel) ──
                    double pwrY  = diagTop + diagBoxH * 0.5;
                    double gndX  = rectLeft + rectW + 22;
                    PLine(panelDim, rectLeft + rectW, pwrY, gndX, pwrY); // horizontal run
                    // Ground symbol (3 decreasing bars)
                    PLine(panelStroke, gndX,     pwrY,     gndX + 10, pwrY);
                    PLine(panelStroke, gndX + 2,  pwrY + 4, gndX + 8,  pwrY + 4);
                    PLine(panelStroke, gndX + 4,  pwrY + 8, gndX + 6,  pwrY + 8);
                    // Label
                    var pwrLabel = new TextBlock
                    {
                        Text = "Toide 230V", FontSize = 6,
                        Foreground = panelDim, IsHitTestVisible = false
                    };
                    Canvas.SetLeft(pwrLabel, gndX + 12);
                    Canvas.SetTop(pwrLabel,  pwrY - 5);
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
                        const double loopH     = 16.0;  // height of each closed-loop rectangle
                        const double wireLeft  = 10.0;  // left margin for loop wires
                        var strokeBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
                        var circleFill  = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                        var circleStroke= new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

                        // Pass 1: find the majority level (most devices) for each loop
                        var loopMajority = new Dictionary<int, (double Elevation, int TotalDevices)>();
                        for (int li2 = 0; li2 < panel.LoopInfos.Count; li2++)
                        {
                            var inf = panel.LoopInfos[li2];
                            if (inf.Levels == null || inf.Levels.Count == 0) continue;
                            var majority    = inf.Levels.OrderByDescending(ld => ld.DeviceCount).First();
                            int totalDevs   = inf.Levels.Sum(ld => ld.DeviceCount);
                            loopMajority[li2] = (majority.Elevation, totalDevs);
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

                            var wireBrush = selected
                                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xC3, 0xF7))
                                : strokeBrush;

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
                            int    wireCount    = _currentVm.GetLoopWireCount(panel.Name, loopInfo.Name);
                            const double wireSpacing = 13.0;
                            double effectiveLoopH    = wireSpacing * (wireCount - 1);
                            double botY    = topY + effectiveLoopH;
                            double farEdge = flipped ? (w - wireRight) : wireLeft;

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

                            if (maj.TotalDevices <= 0) continue;

                            // ── Devices distributed evenly across all wires ───────
                            double span     = Math.Abs(laneX - farEdge);
                            int devRemain   = maj.TotalDevices;
                            for (int wi = 0; wi < wireCount; wi++)
                            {
                                // Ceiling-divide remaining devices among remaining wires
                                int wireDevs = (devRemain + (wireCount - wi) - 1) / (wireCount - wi);
                                double wY    = topY + wi * wireSpacing;
                                double cp    = span / (wireDevs + 1);
                                for (int di = 0; di < wireDevs; di++)
                                {
                                    double devX = flipped
                                        ? farEdge - cp * (di + 1)
                                        : wireLeft + cp * (di + 1);
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

        // ── Level context popup ───────────────────────────────────────────

        private void DiagramCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
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
