using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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

        // Forward and reverse maps between model elements and their WPF shapes
        private readonly Dictionary<SymbolElement, UIElement> _elementToShape
            = new Dictionary<SymbolElement, UIElement>();
        private readonly Dictionary<UIElement, SymbolElement> _shapeToElement
            = new Dictionary<UIElement, SymbolElement>();

        // Dashed cyan overlay drawn on top of the selected shape
        private UIElement _selectionOverlay;

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

        // ─── Constructor ──────────────────────────────────────────────────────

        public SymbolDesignerWindow(SymbolDesignerViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _vm;

            // Wire up ViewModel events
            _vm.Elements.CollectionChanged += Elements_CollectionChanged;
            _vm.CanvasSizeChanged          += OnCanvasSizeChanged;
            _vm.ToolChanged                += OnToolChanged;
            _vm.Saved     += _ => Close();
            _vm.Cancelled += Close;

            // Draw grid once the canvas is laid out
            DrawingCanvas.Loaded += (_, __) => { DrawGrid(); };

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

        private void OnCanvasSizeChanged() => DrawGrid();

        /// <summary>Switch canvas cursor between Arrow (select) and Cross (drawing tools).</summary>
        private void OnToolChanged(DesignerTool tool)
        {
            DrawingCanvas.Cursor = tool == DesignerTool.Select
                ? Cursors.Arrow
                : Cursors.Cross;

            // Cancel any in-progress transform or draw when switching tools
            if (_isDraggingTransform) CancelTransform();
            _isDrawing = false;
            _polyPoints.Clear();
            ClearPreview();
            ClearPolylinePreview();
            _pendingMoveElement = null;

            // Leaving select mode clears the selection highlight
            if (tool != DesignerTool.Select)
            {
                _vm.SelectedElement = null;
                ClearSelectionOverlay();
            }
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
            // RebuildAllDrawnShapes has run; now re-select the new element
            _vm.SelectedElement = newEl;
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

            // ── Select tool ─────────────────────────────────────────────────
            if (_vm.ActiveTool == DesignerTool.Select)
            {
                var selSnap = SnapPoint(e.GetPosition(DrawingCanvas));

                // 1. Transform handle hit?
                var srcFe = e.OriginalSource as FrameworkElement;
                if (srcFe?.Tag is DragMode dm && dm != DragMode.None && _vm.SelectedElement != null)
                {
                    StartTransformDrag(dm, selSnap);
                    e.Handled = true;
                    return;
                }

                // 2. Element hit?
                var hitShape = e.OriginalSource as UIElement;
                if (hitShape != null && _shapeToElement.TryGetValue(hitShape, out var hitEl))
                {
                    _vm.SelectedElement = hitEl;
                    UpdateSelectionOverlay();

                    // Track pending move; if mouse moves >0.5 mm we'll start the move drag
                    _pendingMoveElement  = hitEl;
                    _pendingMoveStartMm  = selSnap;
                }
                else
                {
                    _vm.SelectedElement = null;
                    ClearSelectionOverlay();
                    _pendingMoveElement = null;
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

            // ── Transform drag ───────────────────────────────────────────────
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

            // ── Commit transform drag ────────────────────────────────────────
            if (_isDraggingTransform)
            {
                CommitTransform(snap);
                e.Handled = true;
                return;
            }

            _pendingMoveElement = null;

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
                    CancelTransform();
                }
                else if (_vm.ActiveTool == DesignerTool.Select)
                {
                    // Deselect
                    _vm.SelectedElement = null;
                    ClearSelectionOverlay();
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

            // Delete selected element
            if (e.Key == Key.Delete)
            {
                _vm.DeleteSelectedCommand.Execute(null);
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

            // Tool hotkeys (only when not in text fields)
            if (Keyboard.FocusedElement is TextBox) return;
            switch (e.Key)
            {
                case Key.V: _vm.ActiveTool = DesignerTool.Select;    break;
                case Key.L: _vm.ActiveTool = DesignerTool.Line;      break;
                case Key.P: _vm.ActiveTool = DesignerTool.Polyline;  break;
                case Key.C: _vm.ActiveTool = DesignerTool.Circle;    break;
                case Key.R: _vm.ActiveTool = DesignerTool.Rectangle; break;
            }

            // When tool changes, cancel any in-progress draw
            _isDrawing = false;
            _polyPoints.Clear();
            ClearPreview();
            ClearPolylinePreview();
        }

        // ─── Color swatch click handlers ──────────────────────────────────────

        private void StrokeSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
                _vm.StrokeColor = hex;
        }

        private void FillSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
                _vm.FillColor = hex;
        }

        // ─── Utility ──────────────────────────────────────────────────────────

        /// <summary>Convert canvas pixel position to mm and apply snap.</summary>
        private Point SnapPoint(Point canvasPx)
        {
            double xMm = canvasPx.X / Ppm;
            double yMm = canvasPx.Y / Ppm;
            // Clamp to canvas bounds
            xMm = Math.Max(0, Math.Min(_vm.ViewboxWidthMm, xMm));
            yMm = Math.Max(0, Math.Min(_vm.ViewboxHeightMm, yMm));
            var sp = _vm.Snap(xMm, yMm);
            return new Point(sp.X, sp.Y);
        }

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
