using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Pulse.Core.Settings;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    /// <summary>
    /// Code-behind for the Symbol Designer window.
    /// Handles canvas mouse events, grid rendering, shape preview, and element rendering.
    /// The <see cref="SymbolDesignerViewModel"/> is the data source of truth;
    /// this class translates that data into on-screen WPF visuals.
    /// </summary>
    public partial class SymbolDesignerWindow : Window
    {
        // ─── Scale ────────────────────────────────────────────────────────────
        private const double Ppm = SymbolDesignerViewModel.PixelsPerMm; // px/mm

        // ─── References ───────────────────────────────────────────────────────
        private readonly SymbolDesignerViewModel _vm;

        // ─── Canvas layers (all children of DrawingCanvas) ────────────────────
        private readonly List<UIElement> _gridElements  = new List<UIElement>();

        // ─── Ruler canvas layers ──────────────────────────────────────────────
        private readonly List<UIElement> _rulerHElements = new List<UIElement>();
        private readonly List<UIElement> _rulerVElements = new List<UIElement>();
        private const double RulerThickness = 18.0;

        // Forward and reverse maps between model elements and their WPF shapes
        private readonly Dictionary<SymbolElement, UIElement> _elementToShape
            = new Dictionary<SymbolElement, UIElement>();
        private readonly Dictionary<UIElement, SymbolElement> _shapeToElement
            = new Dictionary<UIElement, SymbolElement>();

        // Dashed cyan overlay drawn on top of the selected shape
        private UIElement _selectionOverlay;

        // ─── Color sync flag (prevents PropertyChanged feedback loop) ─────────
        private bool _syncingColorFromElement;

        // ─── In-progress draw state ───────────────────────────────────────────
        private bool   _isDrawing;
        private Point  _startMm;          // snap-adjusted start point (mm)
        private Point  _lastMm;           // most recent mouse position (mm)
        private readonly List<Point> _polyPoints = new List<Point>(); // polyline vertices (mm)
        private UIElement _previewShape;  // current preview element on the canvas
        private UIElement _polyPreviewLine; // last-segment rubberband for polyline

        // ─── Transform drag state ─────────────────────────────────────────────
        private enum DragMode { None, Move, ScaleNW, ScaleNE, ScaleSE, ScaleSW, Rotate }
        private DragMode _dragMode = DragMode.None;
        private bool _isDraggingTransform;
        private Point _dragStartMm;
        private List<SymbolPoint> _dragOriginalPoints;
        private SymbolElement _dragOriginalElement;
        private UIElement _transformPreviewShape;
        // Pending: mouse-down on a shape — wait to see if it's a click or drag
        private SymbolElement _pendingMoveElement;
        private Point _pendingMoveStartMm;

        // ─── Selection handles ────────────────────────────────────────────────
        private readonly List<UIElement> _handleElements = new List<UIElement>();

        // ─── Multi-selection ────────────────────────────────────────────────
        private readonly HashSet<SymbolElement>  _multiSelection   = new HashSet<SymbolElement>();
        private readonly List<UIElement>          _multiOverlays    = new List<UIElement>();
        // Multi-element move: per-element original point lists
        private Dictionary<SymbolElement, List<SymbolPoint>> _multiDragOriginals;
        private readonly List<UIElement> _multiPreviews = new List<UIElement>();

        // ─── Rubber-band selection ────────────────────────────────────────────
        private bool      _isRubberBanding;
        private Point     _rubberBandStartRaw;   // canvas px, not snapped
        private Rectangle _rubberBandRect;

        // ─── Scale tool (3-point interactive scale) ────────────────────────────
        private int   _scaleToolStep;             // 0=awaiting base, 1=awaiting ref, 2=awaiting target
        private Point _scaleBaseMm;               // base/anchor point (mm)
        private Point _scaleRefMm;                // reference point (mm)
        private readonly List<UIElement> _scaleToolOverlays = new List<UIElement>();

        // ─── Snap cross (movable snap origin) ───────────────────────────────────
        private const string SnapOriginTag        = "SnapOrigin";
        private readonly List<UIElement> _snapCrossElements = new List<UIElement>();
        private Ellipse _snapCrossDot;  // the hit-testable centre dot
        private bool  _isDraggingSnapOrigin;
        private Point _snapOriginDragOffsetMm;   // offset from dot centre to clicked point

        // ─── Constructor ──────────────────────────────────────────────────────

        public SymbolDesignerWindow(SymbolDesignerViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _vm;

            // Wire up ViewModel events
            _vm.Elements.CollectionChanged += Elements_CollectionChanged;
            _vm.PropertyChanged            += Vm_PropertyChanged;
            _vm.CanvasSizeChanged          += OnCanvasSizeChanged;
            _vm.ToolChanged                += OnToolChanged;
            _vm.SnapOriginChanged          += DrawSnapCross;
            _vm.Saved                      += _ => Close();
            _vm.Cancelled                  += Close;
            _vm.SelectAllRequested         += OnSelectAllRequested;

            // Draw grid, snap cross, and any pre-loaded elements once the canvas is laid out
            DrawingCanvas.Loaded += (_, __) => { DrawGrid(); DrawSnapCross(); RebuildAllDrawnShapes(); };

            // Rulers need the full window laid out before drawing (different visual-tree branch)
            Loaded += (_, __) => { DrawRulers(); RebuildCustomSwatches(); };

            // Keyboard shortcuts
            KeyDown += OnKeyDown;
        }

        // ─── Grid drawing ─────────────────────────────────────────────────────

        private void DrawGrid()
        {
            // Remove previous grid elements
            foreach (var el in _gridElements)
                DrawingCanvas.Children.Remove(el);
            _gridElements.Clear();

            double w = _vm.CanvasWidthPx;
            double h = _vm.CanvasHeightPx;
            double minor = _vm.GridSizeMm * Ppm;
            double major = minor * 5.0;

            var minorPen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 0.5);
            var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), 0.5);
            minorPen.Freeze();
            majorPen.Freeze();

            // Vertical lines
            for (double x = 0; x <= w + 0.5; x += minor)
            {
                var isMajor = x % major < 0.5 || (major - x % major) < 0.5;
                var line = new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = h,
                    Stroke          = isMajor ? majorPen.Brush : minorPen.Brush,
                    StrokeThickness = isMajor ? 0.5 : 0.3,
                    IsHitTestVisible = false
                };
                DrawingCanvas.Children.Add(line);
                _gridElements.Add(line);
            }

            // Horizontal lines
            for (double y = 0; y <= h + 0.5; y += minor)
            {
                var isMajor = y % major < 0.5 || (major - y % major) < 0.5;
                var line = new Line
                {
                    X1 = 0, Y1 = y, X2 = w, Y2 = y,
                    Stroke          = isMajor ? majorPen.Brush : minorPen.Brush,
                    StrokeThickness = isMajor ? 0.5 : 0.3,
                    IsHitTestVisible = false
                };
                DrawingCanvas.Children.Add(line);
                _gridElements.Add(line);
            }

            // Origin crosshair labels
            AddGridLabel("0", 2, 2);
        }

        private void AddGridLabel(string text, double x, double y)
        {
            var tb = new TextBlock
            {
                Text      = text,
                FontSize  = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            DrawingCanvas.Children.Add(tb);
            _gridElements.Add(tb);
        }

        private void OnCanvasSizeChanged() { DrawGrid(); DrawRulers(); DrawSnapCross(); }

        // ─── Ruler drawing ────────────────────────────────────────────────────

        private void DrawRulers()
        {
            if (RulerH == null || RulerV == null) return;
            DrawRulerH();
            DrawRulerV();
        }

        private void DrawRulerH()
        {
            foreach (var el in _rulerHElements) RulerH.Children.Remove(el);
            _rulerHElements.Clear();

            var tickBrush  = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            tickBrush.Freeze();
            labelBrush.Freeze();

            double totalMm = _vm.ViewboxWidthMm;
            for (double mm = 0; mm <= totalMm + 0.01; mm += 1.0)
            {
                double x = mm * Ppm;
                bool isMajor = (mm % 10) < 0.01;
                bool isMid   = !isMajor && (mm % 5) < 0.01;
                double tickH = isMajor ? 10 : isMid ? 6 : 3;

                var line = new Line
                {
                    X1 = x, Y1 = RulerThickness, X2 = x, Y2 = RulerThickness - tickH,
                    Stroke = tickBrush, StrokeThickness = isMajor ? 1.0 : 0.5,
                    IsHitTestVisible = false
                };
                RulerH.Children.Add(line);
                _rulerHElements.Add(line);

                if (isMajor && mm >= 0)
                {
                    var tb = new TextBlock
                    {
                        Text = ((int)mm).ToString(),
                        FontSize = 9, Foreground = labelBrush, IsHitTestVisible = false
                    };
                    // Centre label on the tick; small left offset to avoid the edge at x=0
                    Canvas.SetLeft(tb, mm < 0.01 ? 2 : x - 7);
                    Canvas.SetTop(tb, 1);
                    RulerH.Children.Add(tb);
                    _rulerHElements.Add(tb);
                }
            }
        }

        private void DrawRulerV()
        {
            foreach (var el in _rulerVElements) RulerV.Children.Remove(el);
            _rulerVElements.Clear();

            var tickBrush  = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            tickBrush.Freeze();
            labelBrush.Freeze();

            double totalMm = _vm.ViewboxHeightMm;
            for (double mm = 0; mm <= totalMm + 0.01; mm += 1.0)
            {
                double y = mm * Ppm;
                bool isMajor = (mm % 10) < 0.01;
                bool isMid   = !isMajor && (mm % 5) < 0.01;
                double tickW = isMajor ? 10 : isMid ? 6 : 3;

                var line = new Line
                {
                    X1 = RulerThickness, Y1 = y, X2 = RulerThickness - tickW, Y2 = y,
                    Stroke = tickBrush, StrokeThickness = isMajor ? 1.0 : 0.5,
                    IsHitTestVisible = false
                };
                RulerV.Children.Add(line);
                _rulerVElements.Add(line);

                if (isMajor && mm >= 0)
                {
                    var tb = new TextBlock
                    {
                        Text = ((int)mm).ToString(),
                        FontSize = 9, Foreground = labelBrush, IsHitTestVisible = false
                    };
                    Canvas.SetLeft(tb, 1);
                    Canvas.SetTop(tb, mm < 0.01 ? 2 : y - 9);
                    RulerV.Children.Add(tb);
                    _rulerVElements.Add(tb);
                }
            }
        }

        // ─── Select All ───────────────────────────────────────────────────────

        private void OnSelectAllRequested()
        {
            if (_vm.Elements.Count == 0) return;
            _vm.ActiveTool = DesignerTool.Select;
            _multiSelection.Clear();
            foreach (var el in _vm.Elements)
                _multiSelection.Add(el);
            _vm.SelectedElement = null;
            _vm.SelectionInfo = $"{_multiSelection.Count} element{(_multiSelection.Count == 1 ? "" : "s")} selected";
            UpdateMultiSelectionOverlays();
        }

        // ─── Scale ───────────────────────────────────────────────────────────

        private void TbScaleFactor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                ApplyScale();
                e.Handled = true;
            }
        }

        private void BtnScaleApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyScale();
        }

        private void ApplyScale()
        {
            if (!double.TryParse(TbScaleFactor.Text, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out double factor)
                || factor <= 0)
                return;

            // Determine what to scale — multi-selection if active, otherwise everything
            bool hadMultiSelection = _multiSelection.Count > 0;
            var targets = hadMultiSelection
                ? _multiSelection.ToList()
                : _vm.Elements.ToList();

            // Scale and get back the replacement elements (ReplaceElement rebuilds the canvas,
            // which clears _multiSelection and _vm.SelectedElement)
            var replacements = _vm.ScaleElements(targets, factor);

            // Restore the selection on the new (scaled) elements
            if (replacements.Count > 0)
            {
                _multiSelection.Clear();
                foreach (var r in replacements)
                    _multiSelection.Add(r);
                _vm.SelectedElement = null;
                _vm.SelectionInfo = $"{_multiSelection.Count} element{(_multiSelection.Count == 1 ? "" : "s")} selected";
                UpdateMultiSelectionOverlays();
            }
        }

        // ─── Snap cross (movable origin) ──────────────────────────────────────

        private void DrawSnapCross()
        {
            foreach (var el in _snapCrossElements)
                DrawingCanvas.Children.Remove(el);
            _snapCrossElements.Clear();
            _snapCrossDot = null;

            double cx = _vm.SnapOriginXMm * Ppm;
            double cy = _vm.SnapOriginYMm * Ppm;
            double arm = 10;  // pixels each side
            var greenBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xE0, 0x60));
            greenBrush.Freeze();

            var hLine = new Line
            {
                X1 = cx - arm, Y1 = cy, X2 = cx + arm, Y2 = cy,
                Stroke = greenBrush, StrokeThickness = 1.5, IsHitTestVisible = false
            };
            var vLine = new Line
            {
                X1 = cx, Y1 = cy - arm, X2 = cx, Y2 = cy + arm,
                Stroke = greenBrush, StrokeThickness = 1.5, IsHitTestVisible = false
            };

            const double dotR = 5;
            var dot = new Ellipse
            {
                Width = dotR * 2, Height = dotR * 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xFF, 0x80)),
                Stroke = greenBrush, StrokeThickness = 1.2,
                Cursor = Cursors.SizeAll,
                IsHitTestVisible = true,
                Tag = SnapOriginTag,
                ToolTip = "Drag to move snap origin"
            };
            Canvas.SetLeft(dot, cx - dotR);
            Canvas.SetTop( dot, cy - dotR);

            foreach (var l in new UIElement[] { hLine, vLine, dot })
                Panel.SetZIndex(l, 250);

            DrawingCanvas.Children.Add(hLine);
            DrawingCanvas.Children.Add(vLine);
            DrawingCanvas.Children.Add(dot);
            _snapCrossElements.Add(hLine);
            _snapCrossElements.Add(vLine);
            _snapCrossElements.Add(dot);
            _snapCrossDot = dot;
        }

        private void MoveSnapCrossVisual(double xMm, double yMm)
        {
            // Fast update of the cross position without a full rebuild
            if (_snapCrossElements.Count < 3) return;
            double cx = xMm * Ppm, cy = yMm * Ppm, arm = 10, dotR = 5;

            if (_snapCrossElements[0] is Line h) { h.X1 = cx - arm; h.Y1 = cy; h.X2 = cx + arm; h.Y2 = cy; }
            if (_snapCrossElements[1] is Line v) { v.X1 = cx; v.Y1 = cy - arm; v.X2 = cx; v.Y2 = cy + arm; }
            if (_snapCrossElements[2] is Ellipse d)
            {
                Canvas.SetLeft(d, cx - dotR);
                Canvas.SetTop( d, cy - dotR);
            }
        }

        // ─── Multi-selection overlays ─────────────────────────────────────────

        private void ClearMultiSelectionOverlays()
        {
            foreach (var o in _multiOverlays)
                DrawingCanvas.Children.Remove(o);
            _multiOverlays.Clear();
        }

        private void UpdateMultiSelectionOverlays()
        {
            ClearMultiSelectionOverlays();
            foreach (var el in _multiSelection)
            {
                var ov = CreateSelectionOverlay(el);
                if (ov == null) continue;
                Panel.SetZIndex(ov, 201);
                DrawingCanvas.Children.Add(ov);
                _multiOverlays.Add(ov);
            }
            // Update info label
            if (_multiSelection.Count > 1)
                _vm.SelectionInfo = $"{_multiSelection.Count} elements selected";
            else
                _vm.SelectionInfo = "";
        }

        private void ClearAllSelection()
        {
            _vm.SelectedElement = null;
            ClearSelectionOverlay();
            _multiSelection.Clear();
            ClearMultiSelectionOverlays();
            _vm.SelectionInfo = "";
        }

        // ─── Rubber-band selection ────────────────────────────────────────────

        private void StartRubberBand(Point rawPx)
        {
            _isRubberBanding    = true;
            _rubberBandStartRaw = rawPx;

            _rubberBandRect = new Rectangle
            {
                Stroke          = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xAA, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill            = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0xAA, 0xFF)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_rubberBandRect, 500);
            Canvas.SetLeft(_rubberBandRect, rawPx.X);
            Canvas.SetTop( _rubberBandRect, rawPx.Y);
            _rubberBandRect.Width  = 0;
            _rubberBandRect.Height = 0;
            DrawingCanvas.Children.Add(_rubberBandRect);
            DrawingCanvas.CaptureMouse();
        }

        private void UpdateRubberBand(Point rawPx)
        {
            if (_rubberBandRect == null) return;
            double x = Math.Min(_rubberBandStartRaw.X, rawPx.X);
            double y = Math.Min(_rubberBandStartRaw.Y, rawPx.Y);
            double w = Math.Abs(rawPx.X - _rubberBandStartRaw.X);
            double h = Math.Abs(rawPx.Y - _rubberBandStartRaw.Y);
            Canvas.SetLeft(_rubberBandRect, x);
            Canvas.SetTop (_rubberBandRect, y);
            _rubberBandRect.Width  = w;
            _rubberBandRect.Height = h;
        }

        private void CommitRubberBand(Point rawPx)
        {
            DrawingCanvas.ReleaseMouseCapture();
            if (_rubberBandRect != null)
            {
                DrawingCanvas.Children.Remove(_rubberBandRect);
                _rubberBandRect = null;
            }
            _isRubberBanding = false;

            double x1mm = Math.Min(_rubberBandStartRaw.X, rawPx.X) / Ppm;
            double y1mm = Math.Min(_rubberBandStartRaw.Y, rawPx.Y) / Ppm;
            double x2mm = Math.Max(_rubberBandStartRaw.X, rawPx.X) / Ppm;
            double y2mm = Math.Max(_rubberBandStartRaw.Y, rawPx.Y) / Ppm;
            var selRect = new Rect(x1mm, y1mm, x2mm - x1mm, y2mm - y1mm);

            if (selRect.Width < 0.2 && selRect.Height < 0.2) return; // tiny drag = click

            _multiSelection.Clear();
            _vm.SelectedElement = null;
            ClearSelectionOverlay();

            foreach (var el in _vm.Elements)
            {
                var bb = GetBoundingBoxMm(el);
                if (!bb.IsEmpty && selRect.IntersectsWith(bb))
                    _multiSelection.Add(el);
            }

            // Single hit → promote to single selection (enables transforms)
            if (_multiSelection.Count == 1)
            {
                var single = _multiSelection.First();
                _vm.SelectedElement = single;
                _multiSelection.Clear();
                SyncColorsFromElement(single);
                UpdateSelectionOverlay();
            }
            else if (_multiSelection.Count > 1)
            {
                UpdateMultiSelectionOverlays();
            }
        }

        private void CancelRubberBand()
        {
            DrawingCanvas.ReleaseMouseCapture();
            if (_rubberBandRect != null)
            {
                DrawingCanvas.Children.Remove(_rubberBandRect);
                _rubberBandRect = null;
            }
            _isRubberBanding = false;
        }

        // ─── Scale tool (3-point interactive) ────────────────────────────────

        /// <summary>Returns the elements the scale tool should operate on:
        /// current multi-selection if any, otherwise all elements.</summary>
        private IList<SymbolElement> GetScaleToolTargets()
            => _multiSelection.Count > 0
                ? (IList<SymbolElement>)_multiSelection.ToList()
                : _vm.Elements.ToList();

        /// <summary>Remove all transient scale-tool visual overlays from the canvas.</summary>
        private void ClearScaleToolOverlays()
        {
            foreach (var el in _scaleToolOverlays)
                DrawingCanvas.Children.Remove(el);
            _scaleToolOverlays.Clear();
        }

        /// <summary>Cancel/reset the scale tool back to step 0 and clean up visuals.</summary>
        private void CancelScaleTool()
        {
            ClearScaleToolOverlays();
            _scaleToolStep = 0;
            _vm.SelectionInfo = _multiSelection.Count > 0
                ? $"{_multiSelection.Count} elements selected"
                : "";
        }

        /// <summary>Add a small filled circle to the canvas as a scale-point indicator.</summary>
        private UIElement AddScaleDot(double xMm, double yMm, Color color, double radiusPx = 5)
        {
            var dot = new Ellipse
            {
                Width  = radiusPx * 2,
                Height = radiusPx * 2,
                Fill   = new SolidColorBrush(color),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(dot, 300);
            Canvas.SetLeft(dot, xMm * Ppm - radiusPx);
            Canvas.SetTop( dot, yMm * Ppm - radiusPx);
            DrawingCanvas.Children.Add(dot);
            _scaleToolOverlays.Add(dot);
            return dot;
        }

        /// <summary>Add a (optionally dashed) line between two mm-space points as a scale indicator.</summary>
        private UIElement AddScaleLine(Point aMm, Point bMm, Color color, bool dashed = true)
        {
            var line = new Line
            {
                X1 = aMm.X * Ppm, Y1 = aMm.Y * Ppm,
                X2 = bMm.X * Ppm, Y2 = bMm.Y * Ppm,
                Stroke          = new SolidColorBrush(color),
                StrokeThickness = 1.0,
                IsHitTestVisible = false
            };
            if (dashed)
                line.StrokeDashArray = new DoubleCollection { 6, 3 };
            Panel.SetZIndex(line, 298);
            DrawingCanvas.Children.Add(line);
            _scaleToolOverlays.Add(line);
            return line;
        }

        /// <summary>
        /// Rebuild all transient scale-tool overlays (indicator dots, lines, ghost preview).
        /// Called on every MouseMove while the scale tool is active.
        /// </summary>
        private void UpdateScaleToolPreview(Point currentMm)
        {
            ClearScaleToolOverlays();

            // Always show the orange base dot
            AddScaleDot(_scaleBaseMm.X, _scaleBaseMm.Y, Colors.OrangeRed);

            if (_scaleToolStep == 1)
            {
                // Dashed line from base to cursor; shows the reference distance as the user picks
                AddScaleLine(_scaleBaseMm, currentMm, Colors.OrangeRed);
                double refDist = Distance(_scaleBaseMm, currentMm);
                _vm.SelectionInfo = $"Reference: {refDist:F2} mm — click to confirm";
            }
            else if (_scaleToolStep == 2)
            {
                // Fixed line + dot for the reference point
                AddScaleLine(_scaleBaseMm, _scaleRefMm, Color.FromRgb(0xFF, 0xCC, 0x00));
                AddScaleDot(_scaleRefMm.X, _scaleRefMm.Y, Color.FromRgb(0xFF, 0xCC, 0x00));

                // Live line from base to cursor (target)
                AddScaleLine(_scaleBaseMm, currentMm, Colors.White);

                double refDist    = Distance(_scaleBaseMm, _scaleRefMm);
                double targetDist = Distance(_scaleBaseMm, currentMm);
                if (refDist < 0.001) return;
                double factor = targetDist / refDist;

                // Ghost preview of all target elements at the current scale
                foreach (var el in GetScaleToolTargets())
                {
                    var ghost = el.Clone();
                    ghost.Points = el.Points
                        .Select(p => new SymbolPoint(
                            _scaleBaseMm.X + (p.X - _scaleBaseMm.X) * factor,
                            _scaleBaseMm.Y + (p.Y - _scaleBaseMm.Y) * factor))
                        .ToList();
                    var shape = CreateShape(ghost, opacityMultiplier: 0.45);
                    if (shape == null) continue;
                    Panel.SetZIndex(shape, 150);
                    DrawingCanvas.Children.Add(shape);
                    _scaleToolOverlays.Add(shape);
                }

                _vm.SelectionInfo = $"Scale ×{factor:F2} — click to confirm  |  Esc to cancel";
            }
        }

        /// <summary>Commit the scale operation using <paramref name="targetMm"/> as the third point.</summary>
        private void CommitScaleTool(Point targetMm)
        {
            double refDist    = Distance(_scaleBaseMm, _scaleRefMm);
            double targetDist = Distance(_scaleBaseMm, targetMm);
            if (refDist < 0.001 || targetDist < 0.001)
            {
                CancelScaleTool();
                return;
            }

            double factor = targetDist / refDist;
            ClearScaleToolOverlays();

            var targets      = GetScaleToolTargets();
            var replacements = _vm.ScaleElementsFrom(targets, factor, _scaleBaseMm.X, _scaleBaseMm.Y);

            // Re-select replacements if we had a multi-selection
            if (_multiSelection.Count > 0 && replacements.Count > 0)
            {
                _multiSelection.Clear();
                foreach (var r in replacements) _multiSelection.Add(r);
                UpdateMultiSelectionOverlays();
            }

            _vm.SelectionInfo = $"{replacements.Count} element(s) scaled ×{factor:F2}";
            _scaleToolStep = 0;
        }

        // ─── Multi-element move ───────────────────────────────────────────────

        private void StartMultiMove(Point startMm)
        {
            _multiDragOriginals = new Dictionary<SymbolElement, List<SymbolPoint>>();
            foreach (var el in _multiSelection)
                _multiDragOriginals[el] = el.Points.ToList();

            _dragMode    = DragMode.Move;
            _dragStartMm = startMm;
            _isDraggingTransform = true;

            // Hide originals
            foreach (var el in _multiSelection)
                if (_elementToShape.TryGetValue(el, out var s)) s.Visibility = Visibility.Hidden;
            ClearMultiSelectionOverlays();

            DrawingCanvas.CaptureMouse();
        }

        private void UpdateMultiMovePreview(Point currentMm)
        {
            // Clear previous previews
            foreach (var p in _multiPreviews) DrawingCanvas.Children.Remove(p);
            _multiPreviews.Clear();

            double dx = currentMm.X - _dragStartMm.X;
            double dy = currentMm.Y - _dragStartMm.Y;

            foreach (var kv in _multiDragOriginals)
            {
                var moved = new SymbolElement
                {
                    Type = kv.Key.Type, StrokeColor = kv.Key.StrokeColor,
                    StrokeThicknessMm = kv.Key.StrokeThicknessMm,
                    IsFilled = kv.Key.IsFilled, FillColor = kv.Key.FillColor,
                    IsClosed = kv.Key.IsClosed,
                    Points = kv.Value.Select(p => new SymbolPoint(p.X + dx, p.Y + dy)).ToList()
                };
                var shape = CreateShape(moved, opacityMultiplier: 0.75);
                if (shape == null) continue;
                Panel.SetZIndex(shape, 150);
                DrawingCanvas.Children.Add(shape);
                _multiPreviews.Add(shape);
            }
        }

        private void CommitMultiMove(Point currentMm)
        {
            foreach (var p in _multiPreviews) DrawingCanvas.Children.Remove(p);
            _multiPreviews.Clear();

            // Restore visibility before rebuild
            foreach (var el in _multiDragOriginals.Keys)
                if (_elementToShape.TryGetValue(el, out var s)) s.Visibility = Visibility.Visible;

            double dx = currentMm.X - _dragStartMm.X;
            double dy = currentMm.Y - _dragStartMm.Y;

            // Build new elements list; replace all at once
            var replacements = new List<(SymbolElement old, SymbolElement newEl)>();
            foreach (var kv in _multiDragOriginals)
            {
                var newEl = kv.Key.Clone();
                newEl.Points = kv.Value.Select(p => new SymbolPoint(p.X + dx, p.Y + dy)).ToList();
                replacements.Add((kv.Key, newEl));
            }
            var newSelection = new HashSet<SymbolElement>();
            foreach (var (old, newEl) in replacements)
            {
                _vm.ReplaceElement(old, newEl);
                newSelection.Add(newEl);
            }
            _multiSelection.Clear();
            foreach (var el in newSelection) _multiSelection.Add(el);

            ResetTransformDrag();
            _multiDragOriginals = null;
            UpdateMultiSelectionOverlays();
        }

        private void CancelMultiMove()
        {
            foreach (var p in _multiPreviews) DrawingCanvas.Children.Remove(p);
            _multiPreviews.Clear();

            foreach (var el in _multiDragOriginals?.Keys ?? Enumerable.Empty<SymbolElement>())
                if (_elementToShape.TryGetValue(el, out var s)) s.Visibility = Visibility.Visible;

            DrawingCanvas.ReleaseMouseCapture();
            ResetTransformDrag();
            _multiDragOriginals = null;
            UpdateMultiSelectionOverlays();
        }

        /// <summary>Switch canvas cursor between Arrow (select/bucket) and Cross (drawing tools).</summary>
        private void OnToolChanged(DesignerTool tool)
        {
            DrawingCanvas.Cursor = (tool == DesignerTool.Select || tool == DesignerTool.PaintBucket)
                ? Cursors.Arrow
                : Cursors.Cross;

            // Cancel any in-progress transform, multi-move, rubber-band, or scale-tool op
            if (_isDraggingTransform)
            {
                if (_multiDragOriginals != null) CancelMultiMove();
                else CancelTransform();
            }
            if (_isRubberBanding) CancelRubberBand();
            if (_scaleToolStep > 0) CancelScaleTool();
            _isDraggingSnapOrigin = false;
            _isDrawing = false;
            _polyPoints.Clear();
            ClearPreview();
            ClearPolylinePreview();
            _pendingMoveElement = null;

            // Leaving select mode clears all selection
            if (tool != DesignerTool.Select)
                ClearAllSelection();
        }

        // ─── Element rendering ────────────────────────────────────────────────

        private void Elements_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (SymbolElement el in e.NewItems)
                        AddDrawnShape(el);
                    break;

                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    RebuildAllDrawnShapes();
                    break;

                case NotifyCollectionChangedAction.Reset:
                    RebuildAllDrawnShapes();
                    break;
            }

            // Update element count badge
            TbElementCount.Text = $"{_vm.Elements.Count} element{(_vm.Elements.Count == 1 ? "" : "s")}";
        }

        private void RebuildAllDrawnShapes()
        {
            foreach (var s in _elementToShape.Values)
                DrawingCanvas.Children.Remove(s);
            _elementToShape.Clear();
            _shapeToElement.Clear();
            ClearHandles();

            // Keep selection only if the element is still in Elements
            if (_vm.SelectedElement != null && !_vm.Elements.Contains(_vm.SelectedElement))
                _vm.SelectedElement = null;

            ClearSelectionOverlay();

            foreach (var el in _vm.Elements)
                AddDrawnShape(el);

            // Restore selection overlay + handles if something is still selected
            if (_vm.SelectedElement != null)
                UpdateSelectionOverlay();

            // Restore multi-selection overlays (filter to elements still present)
            var stillPresent = _multiSelection.Where(el => _vm.Elements.Contains(el)).ToList();
            _multiSelection.Clear();
            foreach (var el in stillPresent) _multiSelection.Add(el);
            if (_multiSelection.Count > 0) UpdateMultiSelectionOverlays();
        }

        private void AddDrawnShape(SymbolElement el)
        {
            var shape = CreateShape(el, hitTestVisible: true);
            if (shape == null) return;
            DrawingCanvas.Children.Add(shape);
            _elementToShape[el]    = shape;
            _shapeToElement[shape] = el;
        }

        // ─── Shape factory ────────────────────────────────────────────────────

        private static UIElement CreateShape(SymbolElement el, double opacityMultiplier = 1.0,
                                              bool hitTestVisible = false)
        {
            var stroke = ParseBrush(el.StrokeColor, opacityMultiplier);
            var fill   = el.IsFilled
                ? ParseBrush(el.FillColor, opacityMultiplier)
                : Brushes.Transparent;
            double thick = el.StrokeThicknessMm * Ppm;

            switch (el.Type)
            {
                case SymbolElementType.Line:
                {
                    if (el.Points.Count < 2) return null;
                    return new Line
                    {
                        X1 = el.Points[0].X * Ppm,
                        Y1 = el.Points[0].Y * Ppm,
                        X2 = el.Points[1].X * Ppm,
                        Y2 = el.Points[1].Y * Ppm,
                        Stroke           = stroke,
                        StrokeThickness  = thick,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                        IsHitTestVisible = hitTestVisible
                    };
                }

                case SymbolElementType.Polyline:
                {
                    if (el.Points.Count < 2) return null;
                    var pts = new PointCollection(
                        el.Points.Select(p => new Point(p.X * Ppm, p.Y * Ppm)));

                    if (el.IsClosed || el.IsFilled)
                    {
                        return new Polygon
                        {
                            Points          = pts,
                            Stroke          = stroke,
                            StrokeThickness = thick,
                            Fill            = fill,
                            StrokeLineJoin  = PenLineJoin.Round,
                            IsHitTestVisible = hitTestVisible
                        };
                    }
                    return new Polyline
                    {
                        Points          = pts,
                        Stroke          = stroke,
                        StrokeThickness = thick,
                        StrokeLineJoin  = PenLineJoin.Round,
                        IsHitTestVisible = hitTestVisible
                    };
                }

                case SymbolElementType.Circle:
                {
                    if (el.Points.Count < 2) return null;
                    double cx = el.Points[0].X * Ppm;
                    double cy = el.Points[0].Y * Ppm;
                    double ex = el.Points[1].X * Ppm;
                    double ey = el.Points[1].Y * Ppm;
                    double r  = Math.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
                    if (r < 0.5) return null;
                    var ellipse = new Ellipse
                    {
                        Width           = r * 2,
                        Height          = r * 2,
                        Stroke          = stroke,
                        StrokeThickness = thick,
                        Fill            = fill,
                        IsHitTestVisible = hitTestVisible
                    };
                    Canvas.SetLeft(ellipse, cx - r);
                    Canvas.SetTop(ellipse, cy - r);
                    return ellipse;
                }

                case SymbolElementType.Rectangle:
                {
                    if (el.Points.Count < 2) return null;
                    double x1 = Math.Min(el.Points[0].X, el.Points[1].X) * Ppm;
                    double y1 = Math.Min(el.Points[0].Y, el.Points[1].Y) * Ppm;
                    double rw = Math.Abs(el.Points[1].X - el.Points[0].X) * Ppm;
                    double rh = Math.Abs(el.Points[1].Y - el.Points[0].Y) * Ppm;
                    if (rw < 0.5 || rh < 0.5) return null;
                    var rect = new Rectangle
                    {
                        Width           = rw,
                        Height          = rh,
                        Stroke          = stroke,
                        StrokeThickness = thick,
                        Fill            = fill,
                        IsHitTestVisible = hitTestVisible
                    };
                    Canvas.SetLeft(rect, x1);
                    Canvas.SetTop(rect, y1);
                    return rect;
                }
            }
            return null;
        }

        // ─── Selection highlight ────────────────────────────────────────────────

        private void UpdateSelectionOverlay()
        {
            ClearSelectionOverlay();
            if (_vm.SelectedElement == null) return;

            var overlay = CreateSelectionOverlay(_vm.SelectedElement);
            if (overlay == null) return;

            Panel.SetZIndex(overlay, 200);
            DrawingCanvas.Children.Add(overlay);
            _selectionOverlay = overlay;

            // Draw transform handles
            AddHandles(_vm.SelectedElement);
        }

        private void ClearSelectionOverlay()
        {
            if (_selectionOverlay != null)
            {
                DrawingCanvas.Children.Remove(_selectionOverlay);
                _selectionOverlay = null;
            }
            ClearHandles();
        }

        /// <summary>Creates a dashed cyan outline shape over the given element.</summary>
        private static UIElement CreateSelectionOverlay(SymbolElement el)
        {
            var overlay = CreateShape(el, hitTestVisible: false);
            if (!(overlay is Shape s)) return overlay;

            var selBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xD4, 0xFF));
            selBrush.Freeze();
            s.Stroke          = selBrush;
            s.StrokeThickness = Math.Max(el.StrokeThicknessMm * Ppm + 2.5, 3.5);
            s.StrokeDashArray = new DoubleCollection { 5, 3 };
            s.StrokeLineJoin  = PenLineJoin.Round;
            // Subtle fill tint for filled shapes
            if (el.IsFilled)
                s.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0xD4, 0xFF));
            s.Opacity         = 0.85;
            return s;
        }

        // ─── Transform handles ────────────────────────────────────────────────

        private void ClearHandles()
        {
            foreach (var h in _handleElements)
                DrawingCanvas.Children.Remove(h);
            _handleElements.Clear();
        }

        private void AddHandles(SymbolElement el)
        {
            var bbox = GetBoundingBoxMm(el);
            if (bbox.IsEmpty || bbox.Width < 0.01 && bbox.Height < 0.01) return;

            double l = bbox.Left   * Ppm;
            double t = bbox.Top    * Ppm;
            double r = bbox.Right  * Ppm;
            double b = bbox.Bottom * Ppm;
            double mx = (l + r) / 2;
            double my = (t + b) / 2;

            // Corner scale handles
            AddHandle(DragMode.ScaleNW, l, t, Cursors.SizeNWSE);
            AddHandle(DragMode.ScaleNE, r, t, Cursors.SizeNESW);
            AddHandle(DragMode.ScaleSE, r, b, Cursors.SizeNWSE);
            AddHandle(DragMode.ScaleSW, l, b, Cursors.SizeNESW);

            // Rotation handle circle (15 px above top centre)
            double rotCy = t - 18;
            var rotLine = new Line
            {
                X1 = mx, Y1 = t, X2 = mx, Y2 = rotCy + 5,
                Stroke = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0xD4, 0xFF)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(rotLine, 299);
            DrawingCanvas.Children.Add(rotLine);
            _handleElements.Add(rotLine);

            double rd = 10;
            var rotHandle = new Ellipse
            {
                Width  = rd, Height = rd,
                Fill   = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xD4, 0xFF)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
                IsHitTestVisible = true,
                Tag = DragMode.Rotate,
                ToolTip = "Rotate"
            };
            Canvas.SetLeft(rotHandle, mx - rd / 2);
            Canvas.SetTop(rotHandle,  rotCy - rd / 2);
            Panel.SetZIndex(rotHandle, 300);
            DrawingCanvas.Children.Add(rotHandle);
            _handleElements.Add(rotHandle);
        }

        private void AddHandle(DragMode mode, double cxPx, double cyPx, Cursor cursor)
        {
            const double sz = 8;
            string tip = mode == DragMode.ScaleNW ? "Scale NW"
                       : mode == DragMode.ScaleNE ? "Scale NE"
                       : mode == DragMode.ScaleSE ? "Scale SE"
                       : "Scale SW";
            var h = new Rectangle
            {
                Width  = sz, Height = sz,
                Fill   = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x60, 0xA0)),
                StrokeThickness = 1,
                Cursor = cursor,
                IsHitTestVisible = true,
                Tag = mode,
                ToolTip = tip
            };
            Canvas.SetLeft(h, cxPx - sz / 2);
            Canvas.SetTop(h,  cyPx - sz / 2);
            Panel.SetZIndex(h, 300);
            DrawingCanvas.Children.Add(h);
            _handleElements.Add(h);
        }

        // ─── Bounding box helpers ─────────────────────────────────────────────

        private static Rect GetBoundingBoxMm(SymbolElement el)
        {
            if (el == null || el.Points == null || el.Points.Count == 0) return Rect.Empty;

            if (el.Type == SymbolElementType.Circle && el.Points.Count >= 2)
            {
                double cx = el.Points[0].X, cy = el.Points[0].Y;
                double ex = el.Points[1].X, ey = el.Points[1].Y;
                double r  = Math.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
                return new Rect(cx - r, cy - r, r * 2, r * 2);
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in el.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // ─── Transform math helpers ───────────────────────────────────────────

        private static List<SymbolPoint> TranslatePoints(IList<SymbolPoint> pts, double dx, double dy)
            => pts.Select(p => new SymbolPoint(p.X + dx, p.Y + dy)).ToList();

        private static List<SymbolPoint> ScalePoints(IList<SymbolPoint> pts,
                                                      double sx, double sy,
                                                      double anchorX, double anchorY)
            => pts.Select(p => new SymbolPoint(
                anchorX + (p.X - anchorX) * sx,
                anchorY + (p.Y - anchorY) * sy)).ToList();

        private static List<SymbolPoint> RotatePoints(IList<SymbolPoint> pts,
                                                       double angleDeg,
                                                       double cx, double cy)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            return pts.Select(p =>
            {
                double dx = p.X - cx, dy = p.Y - cy;
                return new SymbolPoint(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
            }).ToList();
        }

        // ─── Transform drag ───────────────────────────────────────────────────

        private void StartTransformDrag(DragMode mode, Point startMm)
        {
            _dragMode            = mode;
            _dragStartMm         = startMm;
            _dragOriginalPoints  = _vm.SelectedElement.Points.ToList();
            _dragOriginalElement = _vm.SelectedElement;
            _isDraggingTransform = true;

            // Hide original shape while dragging so only preview is visible
            if (_elementToShape.TryGetValue(_dragOriginalElement, out var origShape))
                origShape.Visibility = Visibility.Hidden;
            if (_selectionOverlay != null)
                _selectionOverlay.Visibility = Visibility.Hidden;

            DrawingCanvas.CaptureMouse();
        }

        private List<SymbolPoint> ComputeTransformedPoints(Point currentMm)
        {
            var bbox = GetBoundingBoxMm(new SymbolElement
            {
                Type   = _dragOriginalElement.Type,
                Points = _dragOriginalPoints
            });

            double dx = currentMm.X - _dragStartMm.X;
            double dy = currentMm.Y - _dragStartMm.Y;

            switch (_dragMode)
            {
                case DragMode.Move:
                    return TranslatePoints(_dragOriginalPoints, dx, dy);

                case DragMode.Rotate:
                {
                    double cx = bbox.IsEmpty ? 0 : bbox.X + bbox.Width  / 2;
                    double cy = bbox.IsEmpty ? 0 : bbox.Y + bbox.Height / 2;
                    double a0 = Math.Atan2(_dragStartMm.Y - cy, _dragStartMm.X - cx);
                    double a1 = Math.Atan2(currentMm.Y    - cy, currentMm.X    - cx);
                    double angleDeg = (a1 - a0) * 180.0 / Math.PI;
                    return RotatePoints(_dragOriginalPoints, angleDeg, cx, cy);
                }

                case DragMode.ScaleNW:
                {
                    if (bbox.IsEmpty) return _dragOriginalPoints.ToList();
                    double ax = bbox.Right, ay = bbox.Bottom;
                    double sx = bbox.Width  > 0.01 ? (ax - currentMm.X) / bbox.Width  : 1;
                    double sy = bbox.Height > 0.01 ? (ay - currentMm.Y) / bbox.Height : 1;
                    sx = Math.Max(0.05, sx); sy = Math.Max(0.05, sy);
                    return ScalePoints(_dragOriginalPoints, sx, sy, ax, ay);
                }

                case DragMode.ScaleNE:
                {
                    if (bbox.IsEmpty) return _dragOriginalPoints.ToList();
                    double ax = bbox.Left, ay = bbox.Bottom;
                    double sx = bbox.Width  > 0.01 ? (currentMm.X - ax) / bbox.Width  : 1;
                    double sy = bbox.Height > 0.01 ? (ay - currentMm.Y) / bbox.Height : 1;
                    sx = Math.Max(0.05, sx); sy = Math.Max(0.05, sy);
                    return ScalePoints(_dragOriginalPoints, sx, sy, ax, ay);
                }

                case DragMode.ScaleSE:
                {
                    if (bbox.IsEmpty) return _dragOriginalPoints.ToList();
                    double ax = bbox.Left, ay = bbox.Top;
                    double sx = bbox.Width  > 0.01 ? (currentMm.X - ax) / bbox.Width  : 1;
                    double sy = bbox.Height > 0.01 ? (currentMm.Y - ay) / bbox.Height : 1;
                    sx = Math.Max(0.05, sx); sy = Math.Max(0.05, sy);
                    return ScalePoints(_dragOriginalPoints, sx, sy, ax, ay);
                }

                case DragMode.ScaleSW:
                {
                    if (bbox.IsEmpty) return _dragOriginalPoints.ToList();
                    double ax = bbox.Right, ay = bbox.Top;
                    double sx = bbox.Width  > 0.01 ? (ax - currentMm.X) / bbox.Width  : 1;
                    double sy = bbox.Height > 0.01 ? (currentMm.Y - ay) / bbox.Height : 1;
                    sx = Math.Max(0.05, sx); sy = Math.Max(0.05, sy);
                    return ScalePoints(_dragOriginalPoints, sx, sy, ax, ay);
                }

                default:
                    return _dragOriginalPoints.ToList();
            }
        }

        private void UpdateTransformPreview(Point currentMm)
        {
            ClearTransformPreview();

            var newPoints = ComputeTransformedPoints(currentMm);
            var previewEl = new SymbolElement
            {
                Type              = _dragOriginalElement.Type,
                Points            = newPoints,
                StrokeColor       = _dragOriginalElement.StrokeColor,
                StrokeThicknessMm = _dragOriginalElement.StrokeThicknessMm,
                IsFilled          = _dragOriginalElement.IsFilled,
                FillColor         = _dragOriginalElement.FillColor,
                IsClosed          = _dragOriginalElement.IsClosed
            };

            var shape = CreateShape(previewEl, opacityMultiplier: 0.75);
            if (shape == null) return;
            Panel.SetZIndex(shape, 150);
            DrawingCanvas.Children.Add(shape);
            _transformPreviewShape = shape;
        }

        private void CommitTransform(Point currentMm)
        {
            var newPoints = ComputeTransformedPoints(currentMm);

            // Restore original visibility before rebuild
            if (_dragOriginalElement != null && _elementToShape.TryGetValue(_dragOriginalElement, out var orig))
                orig.Visibility = Visibility.Visible;
            if (_selectionOverlay != null)
                _selectionOverlay.Visibility = Visibility.Visible;

            ClearTransformPreview();

            var newEl = new SymbolElement
            {
                Type              = _dragOriginalElement.Type,
                Points            = newPoints,
                StrokeColor       = _dragOriginalElement.StrokeColor,
                StrokeThicknessMm = _dragOriginalElement.StrokeThicknessMm,
                IsFilled          = _dragOriginalElement.IsFilled,
                FillColor         = _dragOriginalElement.FillColor,
                IsClosed          = _dragOriginalElement.IsClosed
            };

            _vm.ReplaceElement(_dragOriginalElement, newEl);
            // RebuildAllDrawnShapes has run; find the replacement at the same index
            var dragIdx = _vm.Elements.IndexOf(newEl);
            _vm.SelectedElement = dragIdx >= 0 ? _vm.Elements[dragIdx] : newEl;
            SyncColorsFromElement(_vm.SelectedElement);
            UpdateSelectionOverlay();

            ResetTransformDrag();
        }

        private void CancelTransform()
        {
            if (!_isDraggingTransform) return;

            if (_dragOriginalElement != null && _elementToShape.TryGetValue(_dragOriginalElement, out var orig))
                orig.Visibility = Visibility.Visible;
            if (_selectionOverlay != null)
                _selectionOverlay.Visibility = Visibility.Visible;

            ClearTransformPreview();
            DrawingCanvas.ReleaseMouseCapture();
            ResetTransformDrag();
            UpdateSelectionOverlay();
        }

        private void ClearTransformPreview()
        {
            if (_transformPreviewShape != null)
            {
                DrawingCanvas.Children.Remove(_transformPreviewShape);
                _transformPreviewShape = null;
            }
        }

        private void ResetTransformDrag()
        {
            _isDraggingTransform = false;
            _dragMode            = DragMode.None;
            _dragOriginalPoints  = null;
            _dragOriginalElement = null;
        }

        // ─── Mouse events ─────────────────────────────────────────────────────

        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // ── Snap-origin dot — draggable from any tool mode ─────────────────
            var srcFeGlobal = e.OriginalSource as FrameworkElement;
            if (srcFeGlobal?.Tag is string globalTag && globalTag == SnapOriginTag)
            {
                var rawG = e.GetPosition(DrawingCanvas);
                _isDraggingSnapOrigin   = true;
                _snapOriginDragOffsetMm = new Point(
                    rawG.X / Ppm - _vm.SnapOriginXMm,
                    rawG.Y / Ppm - _vm.SnapOriginYMm);
                DrawingCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // ── Scale tool (3-point click-based scale) ─────────────────────
            if (_vm.ActiveTool == DesignerTool.ScaleTool)
            {
                var stSnap = SnapPoint(e.GetPosition(DrawingCanvas));
                switch (_scaleToolStep)
                {
                    case 0: // pick base point
                        _scaleBaseMm   = stSnap;
                        _scaleToolStep = 1;
                        UpdateScaleToolPreview(stSnap); // show base dot immediately
                        _vm.SelectionInfo = "Click reference point to set reference distance";
                        break;

                    case 1: // pick reference point
                        if (Distance(stSnap, _scaleBaseMm) > 0.1)
                        {
                            _scaleRefMm    = stSnap;
                            _scaleToolStep = 2;
                            UpdateScaleToolPreview(stSnap);
                        }
                        break;

                    case 2: // pick target — commit
                        CommitScaleTool(stSnap);
                        break;
                }
                e.Handled = true;
                return;
            }

            // ── Paint bucket ─────────────────────────────────────────────────
            if (_vm.ActiveTool == DesignerTool.PaintBucket)
            {
                var hitShape = e.OriginalSource as UIElement;
                if (hitShape != null && _shapeToElement.TryGetValue(hitShape, out var hitEl))
                {
                    var filled = hitEl.Clone();
                    filled.IsFilled  = true;
                    filled.FillColor = _vm.FillColor;
                    _vm.ReplaceElement(hitEl, filled);
                }
                e.Handled = true;
                return;
            }

            // ── Select tool ─────────────────────────────────────────────────
            if (_vm.ActiveTool == DesignerTool.Select)
            {
                var raw     = e.GetPosition(DrawingCanvas);
                var selSnap = SnapPoint(raw);
                var srcFe   = e.OriginalSource as FrameworkElement;

                // 1. Transform handle hit? (single-element handles)
                if (srcFe?.Tag is DragMode dm && dm != DragMode.None && _vm.SelectedElement != null)
                {
                    StartTransformDrag(dm, selSnap);
                    e.Handled = true;
                    return;
                }

                // 3. Element hit?
                var hitShape = e.OriginalSource as UIElement;
                if (hitShape != null && _shapeToElement.TryGetValue(hitShape, out var hitEl))
                {
                    bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

                    if (ctrl)
                    {
                        // Toggle membership in multi-selection
                        if (_multiSelection.Contains(hitEl))
                        {
                            _multiSelection.Remove(hitEl);
                        }
                        else
                        {
                            // Absorb any single-select into multi
                            if (_vm.SelectedElement != null)
                            {
                                _multiSelection.Add(_vm.SelectedElement);
                                _vm.SelectedElement = null;
                                ClearSelectionOverlay();
                            }
                            _multiSelection.Add(hitEl);
                        }
                        UpdateMultiSelectionOverlays();
                    }
                    else if (_multiSelection.Count > 1 && _multiSelection.Contains(hitEl))
                    {
                        // Start multi-move immediately
                        StartMultiMove(selSnap);
                    }
                    else
                    {
                        // Normal single select
                        ClearAllSelection();
                        _vm.SelectedElement = hitEl;
                        SyncColorsFromElement(hitEl);
                        UpdateSelectionOverlay();
                        _pendingMoveElement = hitEl;
                        _pendingMoveStartMm = selSnap;
                    }
                }
                else
                {
                    // 4. Empty canvas → start rubber-band
                    ClearAllSelection();
                    StartRubberBand(raw);
                }

                e.Handled = true;
                return;
            }

            // Polyline: double-click finishes the shape
            if (e.ClickCount == 2 && _vm.ActiveTool == DesignerTool.Polyline && _isDrawing)
            {
                // The first of the two MouseDown clicks already added this point — remove it
                if (_polyPoints.Count > 0)
                    _polyPoints.RemoveAt(_polyPoints.Count - 1);

                CommitPolyline();
                e.Handled = true;
                return;
            }

            DrawingCanvas.CaptureMouse();

            var snap = SnapPoint(e.GetPosition(DrawingCanvas));

            switch (_vm.ActiveTool)
            {
                case DesignerTool.Line:
                case DesignerTool.Circle:
                case DesignerTool.Rectangle:
                    _startMm   = snap;
                    _isDrawing = true;
                    break;

                case DesignerTool.Polyline:
                    if (!_isDrawing)
                    {
                        // First point
                        _polyPoints.Clear();
                        _polyPoints.Add(snap);
                        _isDrawing = true;
                    }
                    else
                    {
                        // Subsequent points added on each click
                        _polyPoints.Add(snap);
                    }
                    UpdatePolylinePreview(snap);
                    break;
            }

            e.Handled = true;
        }

        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var raw  = e.GetPosition(DrawingCanvas);
            var snap = SnapPoint(raw);
            _lastMm  = snap;

            // Update status cursor label
            _vm.CursorPosition = $"{snap.X:F1}, {snap.Y:F1} mm";

            // ── Snap-origin drag ─────────────────────────────────────────────
            if (_isDraggingSnapOrigin)
            {
                double xMm = raw.X / Ppm - _snapOriginDragOffsetMm.X;
                double yMm = raw.Y / Ppm - _snapOriginDragOffsetMm.Y;
                xMm = Math.Max(0, Math.Min(_vm.ViewboxWidthMm, xMm));
                yMm = Math.Max(0, Math.Min(_vm.ViewboxHeightMm, yMm));
                var sn = _vm.SnapAbsolute(xMm, yMm); // snap cross to absolute grid
                _vm.SetSnapOrigin(sn.X, sn.Y);
                MoveSnapCrossVisual(sn.X, sn.Y);
                return;
            }

            // ── Rubber-band ──────────────────────────────────────────────────
            if (_isRubberBanding)
            {
                UpdateRubberBand(raw);
                return;
            }

            // ── Multi-element move ───────────────────────────────────────────
            if (_isDraggingTransform && _multiDragOriginals != null)
            {
                UpdateMultiMovePreview(snap);
                return;
            }

            // ── Single transform drag ────────────────────────────────────────
            if (_isDraggingTransform)
            {
                UpdateTransformPreview(snap);
                return;
            }

            // ── Pending move: promote to drag once threshold is reached ───────
            if (_pendingMoveElement != null && e.LeftButton == MouseButtonState.Pressed)
            {
                if (Distance(snap, _pendingMoveStartMm) > 0.5)
                {
                    _vm.SelectedElement = _pendingMoveElement;
                    _pendingMoveElement = null;
                    StartTransformDrag(DragMode.Move, _pendingMoveStartMm);
                    UpdateTransformPreview(snap);
                }
                return;
            }

            if (!_isDrawing) return;

            switch (_vm.ActiveTool)
            {
                case DesignerTool.Line:
                    UpdatePreview(MakePreviewElement(new SymbolElement
                    {
                        Type   = SymbolElementType.Line,
                        Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                        StrokeColor = _vm.StrokeColor,
                        StrokeThicknessMm = _vm.StrokeThicknessMm
                    }));
                    break;

                case DesignerTool.Circle:
                    UpdatePreview(MakePreviewElement(new SymbolElement
                    {
                        Type   = SymbolElementType.Circle,
                        Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                        StrokeColor = _vm.StrokeColor,
                        StrokeThicknessMm = _vm.StrokeThicknessMm,
                        IsFilled = _vm.IsFilled,
                        FillColor = _vm.FillColor
                    }));
                    break;

                case DesignerTool.Rectangle:
                    UpdatePreview(MakePreviewElement(new SymbolElement
                    {
                        Type   = SymbolElementType.Rectangle,
                        Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                        StrokeColor = _vm.StrokeColor,
                        StrokeThicknessMm = _vm.StrokeThicknessMm,
                        IsFilled = _vm.IsFilled,
                        FillColor = _vm.FillColor
                    }));
                    break;

                case DesignerTool.Polyline:
                    // Draw rubberband from last committed point to cursor
                    UpdatePolylinePreview(snap);
                    break;
            }
        }

        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            DrawingCanvas.ReleaseMouseCapture();

            var snap = SnapPoint(e.GetPosition(DrawingCanvas));

            // ── Scale tool — ignore MouseUp (steps handled entirely in MouseDown)
            if (_vm.ActiveTool == DesignerTool.ScaleTool) { e.Handled = true; return; }

            // ── Snap-origin drag commit ──────────────────────────────────────
            if (_isDraggingSnapOrigin)
            {
                _isDraggingSnapOrigin = false;
                DrawingCanvas.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }

            // ── Rubber-band commit ───────────────────────────────────────────
            if (_isRubberBanding)
            {
                CommitRubberBand(e.GetPosition(DrawingCanvas));
                e.Handled = true;
                return;
            }

            // ── Multi-element move commit ────────────────────────────────────
            if (_isDraggingTransform && _multiDragOriginals != null)
            {
                DrawingCanvas.ReleaseMouseCapture();
                CommitMultiMove(snap);
                e.Handled = true;
                return;
            }

            // ── Commit single transform drag ─────────────────────────────────
            if (_isDraggingTransform)
            {
                CommitTransform(snap);
                e.Handled = true;
                return;
            }

            _pendingMoveElement = null;

            // ── Scale tool — MouseMove preview ──────────────────────────────
            if (_vm.ActiveTool == DesignerTool.ScaleTool && _scaleToolStep > 0)
            {
                UpdateScaleToolPreview(snap);
                return;
            }

            if (!_isDrawing) return;

            switch (_vm.ActiveTool)
            {
                case DesignerTool.Line:
                    if (Distance(snap, _startMm) > 0.1) // at least 0.1 mm
                    {
                        _vm.CommitElement(new SymbolElement
                        {
                            Type   = SymbolElementType.Line,
                            Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                            StrokeColor = _vm.StrokeColor,
                            StrokeThicknessMm = _vm.StrokeThicknessMm
                        });
                    }
                    ClearPreview();
                    _isDrawing = false;
                    break;

                case DesignerTool.Circle:
                    if (Distance(snap, _startMm) > 0.1)
                    {
                        _vm.CommitElement(new SymbolElement
                        {
                            Type   = SymbolElementType.Circle,
                            Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                            StrokeColor = _vm.StrokeColor,
                            StrokeThicknessMm = _vm.StrokeThicknessMm,
                            IsFilled = _vm.IsFilled,
                            FillColor = _vm.FillColor
                        });
                    }
                    ClearPreview();
                    _isDrawing = false;
                    break;

                case DesignerTool.Rectangle:
                    if (Distance(snap, _startMm) > 0.1)
                    {
                        _vm.CommitElement(new SymbolElement
                        {
                            Type   = SymbolElementType.Rectangle,
                            Points = new List<SymbolPoint> { ToSP(_startMm), ToSP(snap) },
                            StrokeColor = _vm.StrokeColor,
                            StrokeThicknessMm = _vm.StrokeThicknessMm,
                            IsFilled = _vm.IsFilled,
                            FillColor = _vm.FillColor
                        });
                    }
                    ClearPreview();
                    _isDrawing = false;
                    break;

                // Polyline: committed via double-click — do nothing here
            }
        }

        private void DrawingCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _vm.CursorPosition = string.Empty;
        }

        // ─── Polyline helpers ─────────────────────────────────────────────────

        private void UpdatePolylinePreview(Point cursorMm)
        {
            // Remove old rubberband
            if (_polyPreviewLine != null)
            {
                DrawingCanvas.Children.Remove(_polyPreviewLine);
                _polyPreviewLine = null;
            }

            if (_polyPoints.Count == 0) return;

            // Draw all committed polyline points as a preview polygon/polyline
            var pts = new List<SymbolPoint>(_polyPoints.Select(p => ToSP(p)));
            pts.Add(ToSP(cursorMm)); // add cursor as virtual endpoint

            var previewEl = CreateShape(new SymbolElement
            {
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = _vm.StrokeColor,
                StrokeThicknessMm = _vm.StrokeThicknessMm,
                IsClosed = _vm.IsClosedPath,
                IsFilled = _vm.IsFilled,
                FillColor = _vm.FillColor
            }, opacityMultiplier: 0.55);

            if (previewEl != null)
            {
                DrawingCanvas.Children.Add(previewEl);
                _polyPreviewLine = previewEl;
            }
        }

        private void CommitPolyline()
        {
            if (_polyPoints.Count < 2)
            {
                ClearPolylinePreview();
                _polyPoints.Clear();
                _isDrawing = false;
                return;
            }

            _vm.CommitElement(new SymbolElement
            {
                Type = SymbolElementType.Polyline,
                Points = new List<SymbolPoint>(_polyPoints.Select(p => ToSP(p))),
                StrokeColor = _vm.StrokeColor,
                StrokeThicknessMm = _vm.StrokeThicknessMm,
                IsClosed = _vm.IsClosedPath,
                IsFilled = _vm.IsFilled,
                FillColor = _vm.FillColor
            });

            ClearPolylinePreview();
            _polyPoints.Clear();
            _isDrawing = false;
        }

        private void ClearPolylinePreview()
        {
            if (_polyPreviewLine != null)
            {
                DrawingCanvas.Children.Remove(_polyPreviewLine);
                _polyPreviewLine = null;
            }
        }

        // ─── Single-shape preview helpers ─────────────────────────────────────

        private void UpdatePreview(UIElement shape)
        {
            ClearPreview();
            if (shape != null)
            {
                DrawingCanvas.Children.Add(shape);
                _previewShape = shape;
            }
        }

        private void ClearPreview()
        {
            if (_previewShape != null)
            {
                DrawingCanvas.Children.Remove(_previewShape);
                _previewShape = null;
            }
        }

        private static UIElement MakePreviewElement(SymbolElement el)
            => CreateShape(el, opacityMultiplier: 0.55);

        // ─── Keyboard shortcuts ───────────────────────────────────────────────

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Cancel/abort current drawing or transform operation on Escape
            if (e.Key == Key.Escape)
            {
                if (_isDraggingTransform)
                {
                    if (_multiDragOriginals != null) CancelMultiMove();
                    else CancelTransform();
                }
                else if (_isRubberBanding)
                {
                    CancelRubberBand();
                }
                else if (_vm.ActiveTool == DesignerTool.ScaleTool && _scaleToolStep > 0)
                {
                    CancelScaleTool();
                }
                else if (_vm.ActiveTool == DesignerTool.Select)
                {
                    ClearAllSelection();
                }
                else
                {
                    _isDrawing = false;
                    _polyPoints.Clear();
                    ClearPreview();
                    ClearPolylinePreview();
                }
                e.Handled = true;
                return;
            }

            // Delete selected element(s)
            if (e.Key == Key.Delete)
            {
                if (_multiSelection.Count > 0)
                {
                    _vm.DeleteElements(_multiSelection.ToList());
                    ClearAllSelection();
                }
                else
                {
                    _vm.DeleteSelectedCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            // Undo / Redo
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _vm.UndoCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _vm.RedoCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Select All (Ctrl+A)
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _vm.SelectAllCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Tool hotkeys (only when not in text fields)
            if (Keyboard.FocusedElement is TextBox) return;
            switch (e.Key)
            {
                case Key.V: _vm.ActiveTool = DesignerTool.Select;     break;
                case Key.L: _vm.ActiveTool = DesignerTool.Line;       break;
                case Key.P: _vm.ActiveTool = DesignerTool.Polyline;   break;
                case Key.C: _vm.ActiveTool = DesignerTool.Circle;     break;
                case Key.R: _vm.ActiveTool = DesignerTool.Rectangle;  break;
                case Key.B: _vm.ActiveTool = DesignerTool.PaintBucket; break;
                case Key.S: _vm.ActiveTool = DesignerTool.ScaleTool;  break;
            }

            // When tool changes, cancel any in-progress draw
            _isDrawing = false;
            _polyPoints.Clear();
            ClearPreview();
            ClearPolylinePreview();
        }

        // ─── Color swatch click handlers ──────────────────────────────────────

        private void StrokeColorButton_Click(object sender, RoutedEventArgs e)
        {
            StrokePickerPopup.IsOpen = !StrokePickerPopup.IsOpen;
            if (StrokePickerPopup.IsOpen) FillPickerPopup.IsOpen = false;
        }

        private void FillColorButton_Click(object sender, RoutedEventArgs e)
        {
            FillPickerPopup.IsOpen = !FillPickerPopup.IsOpen;
            if (FillPickerPopup.IsOpen) StrokePickerPopup.IsOpen = false;
        }

        // ─── Color sync helpers ───────────────────────────────────────────────

        /// <summary>
        /// Reads colour properties from <paramref name="el"/> into the VM so
        /// the properties panel and colour picker reflect the selected element.
        /// Sets <see cref="_syncingColorFromElement"/> to suppress the
        /// re-application loop triggered by <see cref="Vm_PropertyChanged"/>.
        /// </summary>
        private void SyncColorsFromElement(SymbolElement el)
        {
            if (el == null) return;
            _syncingColorFromElement = true;
            _vm.StrokeColor = el.StrokeColor ?? "#000000";
            _vm.FillColor   = el.FillColor   ?? "#000000";
            _syncingColorFromElement = false;
        }

        /// <summary>
        /// Listens for VM colour changes (from swatch, hex box, or RGB sliders)
        /// and applies them live to the currently selected single element.
        /// </summary>
        private void Vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_syncingColorFromElement) return;
            if (_vm.SelectedElement == null || _vm.ActiveTool != DesignerTool.Select) return;

            bool isStroke = e.PropertyName == nameof(SymbolDesignerViewModel.StrokeColor)
                            && _vm.SelectedElement.StrokeColor != _vm.StrokeColor;
            bool isFill   = e.PropertyName == nameof(SymbolDesignerViewModel.FillColor)
                            && _vm.SelectedElement.FillColor != _vm.FillColor;

            if (!isStroke && !isFill) return;

            // Remember where the element is before ReplaceElement nulls SelectedElement
            var oldEl = _vm.SelectedElement;
            var idx   = _vm.Elements.IndexOf(oldEl);

            _vm.ApplyColorToElements(
                new[] { oldEl },
                strokeColor: isStroke ? _vm.StrokeColor : null,
                fillColor:   isFill   ? _vm.FillColor   : null);

            // ApplyColorToElements → ReplaceElement → CollectionChanged → RebuildAllDrawnShapes
            // which nulled SelectedElement. Restore it to the new clone at the same index.
            if (idx >= 0 && idx < _vm.Elements.Count)
            {
                _vm.SelectedElement = _vm.Elements[idx];
                UpdateSelectionOverlay();
            }
        }

        private void StrokeSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn && btn.Tag is string hex)) return;
            if (_multiSelection.Count > 0)
            {
                _vm.ApplyColorToElements(_multiSelection, strokeColor: hex);
                UpdateMultiSelectionOverlays();
            }
            else
            {
                _vm.StrokeColor = hex;
            }
        }

        private void FillSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn && btn.Tag is string hex)) return;
            if (_multiSelection.Count > 0)
            {
                _vm.ApplyColorToElements(_multiSelection, fillColor: hex);
                UpdateMultiSelectionOverlays();
            }
            else
            {
                _vm.FillColor = hex;
            }
        }

        // ─── Custom color swatches ──────────────────────────────────────────────────

        private static readonly string _customSwatchesPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse", "symbol-custom-colors.txt");

        // Shared across all designer window instances within an app session.
        private static readonly List<string> _customSwatchColors = LoadCustomSwatchesFromFile();

        private static List<string> LoadCustomSwatchesFromFile()
        {
            try
            {
                if (File.Exists(_customSwatchesPath))
                    return new List<string>(File.ReadAllLines(_customSwatchesPath));
            }
            catch { }
            return new List<string>();
        }

        private static void SaveCustomSwatchesToFile()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_customSwatchesPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_customSwatchesPath, _customSwatchColors);
            }
            catch { }
        }

        /// <summary>Rebuilds both custom swatch WrapPanels from <see cref="_customSwatchColors"/>.</summary>
        private void RebuildCustomSwatches()
        {
            BuildCustomSwatches(StrokeCustomSwatchesPanel, isStroke: true);
            BuildCustomSwatches(FillCustomSwatchesPanel,  isStroke: false);
        }

        private void BuildCustomSwatches(WrapPanel panel, bool isStroke)
        {
            panel.Children.Clear();
            foreach (var hex in _customSwatchColors)
            {
                var h   = hex; // closure capture
                var btn = new Button
                {
                    Tag     = h,
                    ToolTip = h + " (right-click to remove)",
                    Style   = (Style)FindResource("SwatchButtonStyle"),
                };
                try   { btn.Background = new SolidColorBrush((Color)new ColorConverter().ConvertFrom(h)); }
                catch { btn.Background = Brushes.Gray; }
                btn.Click += isStroke
                    ? (RoutedEventHandler)StrokeSwatch_Click
                    : (RoutedEventHandler)FillSwatch_Click;
                btn.MouseRightButtonUp += (s, e) =>
                {
                    _customSwatchColors.Remove(h);
                    SaveCustomSwatchesToFile();
                    RebuildCustomSwatches();
                    e.Handled = true;
                };
                panel.Children.Add(btn);
            }
        }

        private void SaveStrokeSwatch_Click(object sender, RoutedEventArgs e)
        {
            var hex = NormalizeHex(_vm.StrokeColor);
            if (hex == null || _customSwatchColors.Any(c =>
                string.Equals(c, hex, StringComparison.OrdinalIgnoreCase))) return;
            _customSwatchColors.Add(hex);
            SaveCustomSwatchesToFile();
            RebuildCustomSwatches();
        }

        private void SaveFillSwatch_Click(object sender, RoutedEventArgs e)
        {
            var hex = NormalizeHex(_vm.FillColor);
            if (hex == null || _customSwatchColors.Any(c =>
                string.Equals(c, hex, StringComparison.OrdinalIgnoreCase))) return;
            _customSwatchColors.Add(hex);
            SaveCustomSwatchesToFile();
            RebuildCustomSwatches();
        }

        /// <summary>Parses any WPF-valid color string and returns an uppercase #RRGGBB hex string, or null on failure.</summary>
        private static string NormalizeHex(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            try
            {
                var c = (Color)new ColorConverter().ConvertFrom(input.Trim());
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch { return null; }
        }

        // ─── Import DXF / SVG ─────────────────────────────────────────────────

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Import symbol from DXF or SVG",
                Filter      = "Supported files|*.dxf;*.svg|DXF files (*.dxf)|*.dxf|SVG files (*.svg)|*.svg|All files|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog(this) != true) return;

            ImportFromFile(dlg.FileName);
        }

        private void ImportFromFile(string filePath)
        {
            List<SymbolElement> imported;

            try
            {
                string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                imported = ext == ".svg"
                    ? SvgImportService.Import(filePath, _vm.ViewboxWidthMm, _vm.ViewboxHeightMm)
                    : DxfImportService.Import(filePath, _vm.ViewboxWidthMm, _vm.ViewboxHeightMm);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to import: {ex.Message}",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (imported == null || imported.Count == 0)
            {
                MessageBox.Show(
                    "No supported entities were found in the file.",
                    "Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Commit each imported element (adds to undo stack individually)
            foreach (var el in imported)
                _vm.CommitElement(el);
        }

        // ─── Utility ──────────────────────────────────────────────────────────

        /// <summary>Convert canvas pixel position to mm, apply midpoint and grid snap.</summary>
        private Point SnapPoint(Point canvasPx)
        {
            double xMm = canvasPx.X / Ppm;
            double yMm = canvasPx.Y / Ppm;
            // Clamp to canvas bounds
            xMm = Math.Max(0, Math.Min(_vm.ViewboxWidthMm, xMm));
            yMm = Math.Max(0, Math.Min(_vm.ViewboxHeightMm, yMm));

            // 1. Midpoint snap — wins if within 8 px (0.4 mm at 20 px/mm)
            const double midRadiusMm = 8.0 / Ppm;
            var mid = FindNearestMidpoint(xMm, yMm, midRadiusMm);
            if (mid != null) return new Point(mid.X, mid.Y);

            // 2. Grid snap
            var sp = _vm.Snap(xMm, yMm);
            return new Point(sp.X, sp.Y);
        }

        /// <summary>Find the nearest segment midpoint (or circle centre) within radiusMm, or null.</summary>
        private SymbolPoint FindNearestMidpoint(double xMm, double yMm, double radiusMm)
        {
            SymbolPoint best = null;
            double bestDist  = radiusMm;

            foreach (var el in _vm.Elements)
            {
                foreach (var mid in GetSegmentMidpoints(el))
                {
                    double dx = mid.X - xMm, dy = mid.Y - yMm;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < bestDist) { bestDist = d; best = mid; }
                }
            }
            return best;
        }

        private static IEnumerable<SymbolPoint> GetSegmentMidpoints(SymbolElement el)
        {
            if (el?.Points == null) yield break;
            switch (el.Type)
            {
                case SymbolElementType.Line:
                    if (el.Points.Count >= 2)
                        yield return MidSP(el.Points[0], el.Points[1]);
                    break;

                case SymbolElementType.Polyline:
                    for (int i = 0; i < el.Points.Count - 1; i++)
                        yield return MidSP(el.Points[i], el.Points[i + 1]);
                    if (el.IsClosed && el.Points.Count > 2)
                        yield return MidSP(el.Points[el.Points.Count - 1], el.Points[0]);
                    break;

                case SymbolElementType.Rectangle:
                    if (el.Points.Count >= 2)
                    {
                        double x1 = el.Points[0].X, y1 = el.Points[0].Y;
                        double x2 = el.Points[1].X, y2 = el.Points[1].Y;
                        // four side midpoints + centre
                        yield return new SymbolPoint((x1 + x2) / 2, y1);
                        yield return new SymbolPoint((x1 + x2) / 2, y2);
                        yield return new SymbolPoint(x1, (y1 + y2) / 2);
                        yield return new SymbolPoint(x2, (y1 + y2) / 2);
                        yield return new SymbolPoint((x1 + x2) / 2, (y1 + y2) / 2);
                    }
                    break;

                case SymbolElementType.Circle:
                    if (el.Points.Count >= 1)
                        yield return el.Points[0]; // circle centre
                    break;
            }
        }

        private static SymbolPoint MidSP(SymbolPoint a, SymbolPoint b)
            => new SymbolPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static SymbolPoint ToSP(Point p) => new SymbolPoint(p.X, p.Y);

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static SolidColorBrush ParseBrush(string hex, double opacity = 1.0)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color) { Opacity = opacity };
                brush.Freeze();
                return brush;
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent);
            }
        }
    }
}
