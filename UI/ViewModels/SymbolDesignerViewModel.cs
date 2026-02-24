using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Tool modes available in the symbol designer canvas.
    /// </summary>
    public enum DesignerTool
    {
        Select,
        Line,
        Polyline,
        Circle,
        Rectangle,
        PaintBucket,
        /// <summary>3-point interactive scale: click base → reference → target.</summary>
        ScaleTool
    }

    /// <summary>
    /// ViewModel for the custom symbol designer window.
    /// Holds drawing state, the completed element list, and undo/redo history.
    /// Canvas mouse logic lives in the code-behind; this VM is the source of truth for data.
    /// </summary>
    public class SymbolDesignerViewModel : ViewModelBase
    {
        // ─── Undo / Redo ─────────────────────────────────────────────────────

        private readonly Stack<SymbolElement> _undoStack = new Stack<SymbolElement>();
        private readonly Stack<SymbolElement> _redoStack = new Stack<SymbolElement>();

        // ─── Element list ────────────────────────────────────────────────────

        /// <summary>All committed drawing elements, in draw order.</summary>
        public ObservableCollection<SymbolElement> Elements { get; }
            = new ObservableCollection<SymbolElement>();

        // ─── Symbol metadata ─────────────────────────────────────────────────

        /// <summary>
        /// When non-null this designer is editing an existing symbol;
        /// <see cref="ExecuteSave"/> will reuse this Id instead of generating a new one.
        /// </summary>
        private string _existingId;

        private string _symbolName = "New Symbol";
        public string SymbolName
        {
            get => _symbolName;
            set => SetField(ref _symbolName, value);
        }

        private double _viewboxWidthMm = 20.0;
        public double ViewboxWidthMm
        {
            get => _viewboxWidthMm;
            set
            {
                if (SetField(ref _viewboxWidthMm, Math.Max(5.0, Math.Min(200.0, value))))
                {
                    OnPropertyChanged(nameof(CanvasWidthPx));
                    CanvasSizeChanged?.Invoke();
                }
            }
        }

        private double _viewboxHeightMm = 20.0;
        public double ViewboxHeightMm
        {
            get => _viewboxHeightMm;
            set
            {
                if (SetField(ref _viewboxHeightMm, Math.Max(5.0, Math.Min(200.0, value))))
                {
                    OnPropertyChanged(nameof(CanvasHeightPx));
                    CanvasSizeChanged?.Invoke();
                }
            }
        }

        /// <summary>Canvas pixel width derived from viewbox (20 px/mm).</summary>
        public double CanvasWidthPx => ViewboxWidthMm * PixelsPerMm;

        /// <summary>Canvas pixel height derived from viewbox (20 px/mm).</summary>
        public double CanvasHeightPx => ViewboxHeightMm * PixelsPerMm;

        /// <summary>Pixels per millimetre for the designer canvas.</summary>
        public const double PixelsPerMm = 20.0;

        // ─── Active tool ─────────────────────────────────────────────────────

        private DesignerTool _activeTool = DesignerTool.Line;
        public DesignerTool ActiveTool
        {
            get => _activeTool;
            set
            {
                if (SetField(ref _activeTool, value))
                    ToolChanged?.Invoke(value);
            }
        }

        // ─── Drawing properties ───────────────────────────────────────────────

        private string _strokeColor = "#FF0000";
        public string StrokeColor
        {
            get => _strokeColor;
            set
            {
                if (SetField(ref _strokeColor, value))
                {
                    OnPropertyChanged(nameof(StrokeR));
                    OnPropertyChanged(nameof(StrokeG));
                    OnPropertyChanged(nameof(StrokeB));
                }
            }
        }

        private double _strokeThicknessMm = 0.5;
        public double StrokeThicknessMm
        {
            get => _strokeThicknessMm;
            set => SetField(ref _strokeThicknessMm, Math.Max(0.1, Math.Min(5.0, value)));
        }

        private bool _isFilled;
        public bool IsFilled
        {
            get => _isFilled;
            set => SetField(ref _isFilled, value);
        }

        private string _fillColor = "#FF0000";
        public string FillColor
        {
            get => _fillColor;
            set
            {
                if (SetField(ref _fillColor, value))
                {
                    OnPropertyChanged(nameof(FillR));
                    OnPropertyChanged(nameof(FillG));
                    OnPropertyChanged(nameof(FillB));
                }
            }
        }

        // ─── Color picker RGB components ──────────────────────────────────────

        /// <summary>Red component (0-255) of StrokeColor, synced bidirectionally.</summary>
        public int StrokeR
        {
            get { var c = TryParseHex(_strokeColor); return c?.R ?? 0; }
            set => ApplyColorComponent(ref _strokeColor, nameof(StrokeColor), nameof(StrokeR), r: value);
        }

        /// <summary>Green component (0-255) of StrokeColor, synced bidirectionally.</summary>
        public int StrokeG
        {
            get { var c = TryParseHex(_strokeColor); return c?.G ?? 0; }
            set => ApplyColorComponent(ref _strokeColor, nameof(StrokeColor), nameof(StrokeG), g: value);
        }

        /// <summary>Blue component (0-255) of StrokeColor, synced bidirectionally.</summary>
        public int StrokeB
        {
            get { var c = TryParseHex(_strokeColor); return c?.B ?? 0; }
            set => ApplyColorComponent(ref _strokeColor, nameof(StrokeColor), nameof(StrokeB), b: value);
        }

        /// <summary>Red component (0-255) of FillColor, synced bidirectionally.</summary>
        public int FillR
        {
            get { var c = TryParseHex(_fillColor); return c?.R ?? 0; }
            set => ApplyColorComponent(ref _fillColor, nameof(FillColor), nameof(FillR), r: value);
        }

        /// <summary>Green component (0-255) of FillColor, synced bidirectionally.</summary>
        public int FillG
        {
            get { var c = TryParseHex(_fillColor); return c?.G ?? 0; }
            set => ApplyColorComponent(ref _fillColor, nameof(FillColor), nameof(FillG), g: value);
        }

        /// <summary>Blue component (0-255) of FillColor, synced bidirectionally.</summary>
        public int FillB
        {
            get { var c = TryParseHex(_fillColor); return c?.B ?? 0; }
            set => ApplyColorComponent(ref _fillColor, nameof(FillColor), nameof(FillB), b: value);
        }

        private void ApplyColorComponent(ref string field, string hexPropName, string componentPropName,
            int? r = null, int? g = null, int? b = null)
        {
            var c = TryParseHex(field);
            int nr = Math.Max(0, Math.Min(255, r ?? c?.R ?? 0));
            int ng = Math.Max(0, Math.Min(255, g ?? c?.G ?? 0));
            int nb = Math.Max(0, Math.Min(255, b ?? c?.B ?? 0));
            var newHex = $"#{nr:X2}{ng:X2}{nb:X2}";
            if (field == newHex) return;
            field = newHex;
            OnPropertyChanged(hexPropName);
            OnPropertyChanged(componentPropName);
        }

        private static (byte R, byte G, byte B)? TryParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            var h = hex.TrimStart('#');
            if (h.Length == 6 &&
                uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return ((byte)(val >> 16), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF));
            return null;
        }

        private bool _isClosedPath;
        public bool IsClosedPath
        {
            get => _isClosedPath;
            set => SetField(ref _isClosedPath, value);
        }

        // ─── Grid / snap ──────────────────────────────────────────────────────

        private bool _snapToGrid = true;
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => SetField(ref _snapToGrid, value);
        }

        private double _gridSizeMm = 1.0;
        public double GridSizeMm
        {
            get => _gridSizeMm;
            set
            {
                if (SetField(ref _gridSizeMm, Math.Max(0.5, Math.Min(10.0, value))))
                    CanvasSizeChanged?.Invoke(); // triggers grid redraw
            }
        }

        // ─── Snap origin ──────────────────────────────────────────────────────

        private double _snapOriginXMm = 0.0;
        public double SnapOriginXMm
        {
            get => _snapOriginXMm;
            set { if (SetField(ref _snapOriginXMm, value)) SnapOriginChanged?.Invoke(); }
        }

        private double _snapOriginYMm = 0.0;
        public double SnapOriginYMm
        {
            get => _snapOriginYMm;
            set { if (SetField(ref _snapOriginYMm, value)) SnapOriginChanged?.Invoke(); }
        }

        /// <summary>Update both snap-origin coordinates and fire SnapOriginChanged once.</summary>
        public void SetSnapOrigin(double xMm, double yMm)
        {
            bool changed = false;
            if (_snapOriginXMm != xMm) { _snapOriginXMm = xMm; OnPropertyChanged(nameof(SnapOriginXMm)); changed = true; }
            if (_snapOriginYMm != yMm) { _snapOriginYMm = yMm; OnPropertyChanged(nameof(SnapOriginYMm)); changed = true; }
            if (changed) SnapOriginChanged?.Invoke();
        }

        // ─── Selection info ───────────────────────────────────────────────────

        private string _selectionInfo = "";
        /// <summary>Human-readable multi-selection count shown in the properties panel.</summary>
        public string SelectionInfo
        {
            get => _selectionInfo;
            set => SetField(ref _selectionInfo, value);
        }

        // ─── Status label ─────────────────────────────────────────────────────

        private string _cursorPosition = "0.0, 0.0 mm";
        public string CursorPosition
        {
            get => _cursorPosition;
            set => SetField(ref _cursorPosition, value);
        }

        // ─── Commands ─────────────────────────────────────────────────────────

        // ─── Selection ────────────────────────────────────────────────────────

        private SymbolElement _selectedElement;
        /// <summary>Currently selected element in Select mode. Null when nothing is selected.</summary>
        public SymbolElement SelectedElement
        {
            get => _selectedElement;
            set
            {
                SetField(ref _selectedElement, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand SelectToolCommand      { get; }
        public ICommand LineToolCommand        { get; }
        public ICommand PolylineToolCommand    { get; }
        public ICommand CircleToolCommand      { get; }
        public ICommand RectangleToolCommand   { get; }
        public ICommand PaintBucketToolCommand { get; }
        public ICommand UndoCommand            { get; }
        public ICommand RedoCommand            { get; }
        public ICommand ClearCommand           { get; }
        public ICommand DeleteSelectedCommand  { get; }
        public ICommand SaveCommand            { get; }
        public ICommand CancelCommand          { get; }
        /// <summary>Fires <see cref="SelectAllRequested"/> so the code-behind can select every element.</summary>
        public ICommand ScaleToolCommand { get; }
        public ICommand SelectAllCommand { get; }

        // ─── Events ───────────────────────────────────────────────────────────

        /// <summary>Raised when the save button is pressed. Carries the completed definition.</summary>
        public event Action<CustomSymbolDefinition> Saved;

        /// <summary>Raised on Cancel.</summary>
        public event Action Cancelled;

        /// <summary>Raised when the active tool changes.</summary>
        public event Action<DesignerTool> ToolChanged;

        /// <summary>Raised when canvas size or grid settings change (triggers grid redraw).</summary>
        public event Action CanvasSizeChanged;

        /// <summary>Raised when the snap origin (SnapOriginXMm / SnapOriginYMm) changes.</summary>
        public event Action SnapOriginChanged;

        /// <summary>Raised by <see cref="SelectAllCommand"/>. The code-behind handler selects every element into the multi-selection set.</summary>
        public event Action SelectAllRequested;

        // ─── Constructor ──────────────────────────────────────────────────────

        public SymbolDesignerViewModel()
        {
            SelectToolCommand      = new RelayCommand(_ => ActiveTool = DesignerTool.Select);
            LineToolCommand        = new RelayCommand(_ => ActiveTool = DesignerTool.Line);
            PolylineToolCommand    = new RelayCommand(_ => ActiveTool = DesignerTool.Polyline);
            CircleToolCommand      = new RelayCommand(_ => ActiveTool = DesignerTool.Circle);
            RectangleToolCommand   = new RelayCommand(_ => ActiveTool = DesignerTool.Rectangle);
            PaintBucketToolCommand = new RelayCommand(_ => ActiveTool = DesignerTool.PaintBucket);
            ScaleToolCommand      = new RelayCommand(_ => ActiveTool = DesignerTool.ScaleTool);

            UndoCommand           = new RelayCommand(_ => ExecuteUndo(),    _ => _undoStack.Count > 0);
            RedoCommand           = new RelayCommand(_ => ExecuteRedo(),    _ => _redoStack.Count > 0);
            ClearCommand          = new RelayCommand(_ => ExecuteClear(),   _ => Elements.Count > 0);
            DeleteSelectedCommand = new RelayCommand(_ => ExecuteDeleteSelected(), _ => _selectedElement != null);
            SaveCommand           = new RelayCommand(_ => ExecuteSave(),    _ => !string.IsNullOrWhiteSpace(SymbolName));
            CancelCommand         = new RelayCommand(_ => Cancelled?.Invoke());
            SelectAllCommand      = new RelayCommand(_ => SelectAllRequested?.Invoke(), _ => Elements.Count > 0);
        }

        // ─── Public API used by code-behind ───────────────────────────────────

        /// <summary>
        /// Adds a completed element to the list (committed on mouse-up / double-click).
        /// Clears the redo stack as in any standard undo system.
        /// </summary>
        public void CommitElement(SymbolElement element)
        {
            if (element == null) return;
            Elements.Add(element);
            _undoStack.Push(element);
            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Pre-populates this designer with an existing symbol definition so the user can edit it.
        /// Call before showing the designer window.
        /// </summary>
        public void LoadFrom(CustomSymbolDefinition def)
        {
            if (def == null) return;

            _existingId     = def.Id;
            SymbolName      = def.Name;
            ViewboxWidthMm  = def.ViewboxWidthMm;
            ViewboxHeightMm = def.ViewboxHeightMm;
            SetSnapOrigin(def.SnapOriginXMm, def.SnapOriginYMm);

            Elements.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            foreach (var el in def.Elements)
                Elements.Add(el.Clone());

            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Snaps a millimetre coordinate to the current grid, relative to the snap origin.</summary>
        public SymbolPoint Snap(double xMm, double yMm)
        {
            if (!SnapToGrid) return new SymbolPoint(xMm, yMm);
            var s = GridSizeMm;
            double ox = SnapOriginXMm, oy = SnapOriginYMm;
            return new SymbolPoint(
                Math.Round((xMm - ox) / s) * s + ox,
                Math.Round((yMm - oy) / s) * s + oy);
        }

        /// <summary>Snap to absolute grid (origin always 0,0) — used when dragging the snap-origin cross itself.</summary>
        public SymbolPoint SnapAbsolute(double xMm, double yMm)
        {
            if (!SnapToGrid) return new SymbolPoint(xMm, yMm);
            var s = GridSizeMm;
            return new SymbolPoint(Math.Round(xMm / s) * s, Math.Round(yMm / s) * s);
        }

        /// <summary>
        /// Applies a new stroke and/or fill colour to a set of elements.
        /// Replaces each element in-place so undo works correctly.
        /// Pass null for a colour to leave it unchanged.
        /// </summary>
        public void ApplyColorToElements(IEnumerable<SymbolElement> elements,
                                          string strokeColor = null,
                                          string fillColor   = null)
        {
            foreach (var el in elements.ToList())
            {
                var newEl = el.Clone();
                if (strokeColor != null) newEl.StrokeColor = strokeColor;
                if (fillColor   != null) newEl.FillColor   = fillColor;
                ReplaceElement(el, newEl);
            }
        }

        /// <summary>Removes a set of elements from the canvas and clears the undo/redo stacks for them.</summary>
        public void DeleteElements(IEnumerable<SymbolElement> toDelete)
        {
            var set = new HashSet<SymbolElement>(toDelete);
            foreach (var el in set.ToList()) Elements.Remove(el);

            var remaining = new List<SymbolElement>(_undoStack.Where(e => !set.Contains(e)));
            remaining.Reverse();
            _undoStack.Clear();
            foreach (var e in remaining) _undoStack.Push(e);
            _redoStack.Clear();

            SelectedElement = null;
            CommandManager.InvalidateRequerySuggested();
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        private void ExecuteDeleteSelected()
        {
            if (_selectedElement == null) return;
            var toDelete = _selectedElement;
            SelectedElement = null;
            Elements.Remove(toDelete);
            // Remove from undo stack so it can't be re-added via redo
            var remaining = new List<SymbolElement>(_undoStack.Where(e => e != toDelete));
            remaining.Reverse();
            _undoStack.Clear();
            foreach (var e in remaining) _undoStack.Push(e);
            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteUndo()
        {
            if (_undoStack.Count == 0) return;
            var last = _undoStack.Pop();
            Elements.Remove(last);
            _redoStack.Push(last);
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteRedo()
        {
            if (_redoStack.Count == 0) return;
            var el = _redoStack.Pop();
            Elements.Add(el);
            _undoStack.Push(el);
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteClear()
        {
            // Push each element onto undo stack so the whole clear can be unwound
            foreach (var el in Elements)
                _undoStack.Push(el);
            Elements.Clear();
            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Scales the given elements' points and stroke widths by <paramref name="factor"/>,
        /// relative to an explicit <paramref name="basePtX"/>/<paramref name="basePtY"/> anchor.
        /// Uses ReplaceElement so the operation is undoable.
        /// Returns the list of replacement elements in input order so the caller can re-select them.
        /// </summary>
        public IList<SymbolElement> ScaleElementsFrom(IEnumerable<SymbolElement> elements,
            double factor, double basePtX, double basePtY)
        {
            var replacements = new List<SymbolElement>();
            if (elements == null || Math.Abs(factor - 1.0) < 0.00001) return replacements;
            foreach (var el in elements.ToList())
            {
                var scaled = el.Clone();
                scaled.StrokeThicknessMm = el.StrokeThicknessMm * factor;
                scaled.Points = el.Points
                    .Select(p => new SymbolPoint(
                        basePtX + (p.X - basePtX) * factor,
                        basePtY + (p.Y - basePtY) * factor))
                    .ToList();
                ReplaceElement(el, scaled);
                replacements.Add(scaled);
            }
            return replacements;
        }

        /// <summary>
        /// Scales the given elements' points and stroke widths by <paramref name="factor"/>,
        /// relative to the snap origin.  Uses ReplaceElement so the operation is undoable.
        /// Returns the list of replacement elements in input order so the caller can
        /// re-select them after the rebuild.
        /// </summary>
        public IList<SymbolElement> ScaleElements(IEnumerable<SymbolElement> elements, double factor)
        {
            var replacements = new List<SymbolElement>();
            if (elements == null || Math.Abs(factor - 1.0) < 0.00001) return replacements;
            double ox = SnapOriginXMm;
            double oy = SnapOriginYMm;
            foreach (var el in elements.ToList())
            {
                var scaled = el.Clone();
                scaled.StrokeThicknessMm = el.StrokeThicknessMm * factor;
                scaled.Points = el.Points
                    .Select(p => new SymbolPoint(
                        ox + (p.X - ox) * factor,
                        oy + (p.Y - oy) * factor))
                    .ToList();
                ReplaceElement(el, scaled);
                replacements.Add(scaled);
            }
            return replacements;
        }

        private void ExecuteSave()
        {
            var def = new CustomSymbolDefinition
            {
                Id               = _existingId ?? Guid.NewGuid().ToString(),
                Name             = SymbolName.Trim(),
                ViewboxWidthMm   = ViewboxWidthMm,
                ViewboxHeightMm  = ViewboxHeightMm,
                SnapOriginXMm    = SnapOriginXMm,
                SnapOriginYMm    = SnapOriginYMm,
                Elements         = new List<SymbolElement>(Elements)
            };
            Saved?.Invoke(def);
        }

        /// <summary>
        /// Replaces an existing element in the list with a new (transformed) version.
        /// Patches the undo stack so the replacement is undoable.
        /// </summary>
        public void ReplaceElement(SymbolElement old, SymbolElement replacement)
        {
            var idx = Elements.IndexOf(old);
            if (idx < 0) return;

            Elements[idx] = replacement;

            // Swap old for replacement in the undo stack
            var items = new List<SymbolElement>(_undoStack);
            items.Reverse();
            _undoStack.Clear();
            foreach (var e in items)
                _undoStack.Push(ReferenceEquals(e, old) ? replacement : e);

            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
