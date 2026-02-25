using System;
using System.Collections.Generic;

namespace Pulse.Core.Graph.Canvas
{
    /// <summary>
    /// Complete logical description of a diagram canvas frame.
    /// Built from ViewModel data; consumed by the renderer.
    /// Contains no pixel positions — layout is a separate concern.
    /// </summary>
    public sealed class DiagramScene
    {
        public IReadOnlyList<LevelAnchor> Levels { get; }
        public IReadOnlyList<PanelCluster> Panels { get; }
        public IReadOnlyList<DiagramOverlay> Overlays { get; }

        public DiagramScene(
            IReadOnlyList<LevelAnchor> levels,
            IReadOnlyList<PanelCluster> panels,
            IReadOnlyList<DiagramOverlay> overlays)
        {
            Levels   = levels   ?? Array.Empty<LevelAnchor>();
            Panels   = panels   ?? Array.Empty<PanelCluster>();
            Overlays = overlays ?? Array.Empty<DiagramOverlay>();
        }

        /// <summary>Empty scene with no content.</summary>
        public static readonly DiagramScene Empty =
            new DiagramScene(null, null, null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Level anchors
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A horizontal level reference line — the Y-axis backbone of the diagram.
    /// </summary>
    public sealed class LevelAnchor
    {
        public string Name           { get; }
        public double ElevationFeet  { get; }

        /// <summary>Whether the dashed level line itself is visible.</summary>
        public bool LineVisible      { get; }
        /// <summary>Whether this level's own name label (above the line) is visible.</summary>
        public bool TextAboveVisible { get; }
        /// <summary>Whether the previous level's name label (below the line) is visible.</summary>
        public bool TextBelowVisible { get; }

        /// <summary>Label text drawn above the line (this level's name).</summary>
        public string TextAbove { get; }
        /// <summary>Label text drawn below the line (previous level's name, or null for the lowest level).</summary>
        public string TextBelow { get; }

        public LevelAnchor(
            string name, double elevationFeet,
            bool lineVisible, bool textAboveVisible, bool textBelowVisible,
            string textAbove, string textBelow)
        {
            Name             = name;
            ElevationFeet    = elevationFeet;
            LineVisible      = lineVisible;
            TextAboveVisible = textAboveVisible;
            TextBelowVisible = textBelowVisible;
            TextAbove        = textAbove;
            TextBelow        = textBelow;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Panel / Loop clusters
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A panel column containing its symbol, output cells, and child loop clusters.
    /// One panel occupies a vertical strip of the canvas.
    /// </summary>
    public sealed class PanelCluster
    {
        public string  Name            { get; }
        public double? ElevationFeet   { get; }
        /// <summary>MaxLoopCount from the assigned ControlPanelConfig (0 = no config).</summary>
        public int     ConfigLoopCount { get; }
        public IReadOnlyList<LoopCluster> Loops { get; }

        public PanelCluster(
            string name, double? elevationFeet, int configLoopCount,
            IReadOnlyList<LoopCluster> loops)
        {
            Name            = name;
            ElevationFeet   = elevationFeet;
            ConfigLoopCount = configLoopCount;
            Loops           = loops ?? Array.Empty<LoopCluster>();
        }
    }

    /// <summary>
    /// A loop's wire serpentine and all its devices.
    /// Contains both per-level rows (for the wire layout) and a flat
    /// address-ordered device list (for symbol placement along the wire).
    /// </summary>
    public sealed class LoopCluster
    {
        public string LoopName  { get; }
        /// <summary>Composite key "panelName::loopName".</summary>
        public string Key       { get; }
        public bool   IsFlipped { get; }
        /// <summary>Total horizontal wires (min 2 = top + bottom).</summary>
        public int    WireCount { get; }
        /// <summary>Display rank within its side/elevation group (0 = bottommost).</summary>
        public int    Rank      { get; }
        /// <summary>Hex colour string for the wire, or null (default black).</summary>
        public string WireColor { get; }

        /// <summary>Per-elevation device breakdown (grouped by level).</summary>
        public IReadOnlyList<DeviceRow> Rows { get; }
        /// <summary>Flat device list ordered by numeric address ascending.</summary>
        public IReadOnlyList<DeviceSlot> Devices { get; }

        public LoopCluster(
            string loopName, string key, bool isFlipped,
            int wireCount, int rank, string wireColor,
            IReadOnlyList<DeviceRow> rows,
            IReadOnlyList<DeviceSlot> devices)
        {
            LoopName  = loopName;
            Key       = key;
            IsFlipped = isFlipped;
            WireCount = wireCount;
            Rank      = rank;
            WireColor = wireColor;
            Rows      = rows    ?? Array.Empty<DeviceRow>();
            Devices   = devices ?? Array.Empty<DeviceSlot>();
        }
    }

    /// <summary>
    /// Devices grouped at one elevation on a single loop.
    /// </summary>
    public sealed class DeviceRow
    {
        public double ElevationFeet { get; }
        public int    DeviceCount   { get; }
        /// <summary>Per-type breakdown at this elevation, sorted by DeviceType.</summary>
        public IReadOnlyList<(string DeviceType, int Count)> TypeBreakdown { get; }

        public DeviceRow(
            double elevationFeet, int deviceCount,
            IReadOnlyList<(string DeviceType, int Count)> typeBreakdown)
        {
            ElevationFeet = elevationFeet;
            DeviceCount   = deviceCount;
            TypeBreakdown = typeBreakdown ?? Array.Empty<(string, int)>();
        }
    }

    /// <summary>
    /// A single device positioned on a wire, identified by its address.
    /// </summary>
    public sealed class DeviceSlot
    {
        public string Address    { get; }
        public string DeviceType { get; }
        /// <summary>Resolved custom symbol key from SymbolMappings, or null for default circle.</summary>
        public string SymbolKey  { get; }

        public DeviceSlot(string address, string deviceType, string symbolKey)
        {
            Address    = address    ?? string.Empty;
            DeviceType = deviceType ?? string.Empty;
            SymbolKey  = symbolKey;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Overlays
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An overlay annotation attached to a diagram element.
    /// Overlays are rendered on top of the base diagram.
    /// </summary>
    public sealed class DiagramOverlay
    {
        public OverlayKind Kind      { get; }
        /// <summary>Key of the target element (e.g. "PanelA::Loop1", a level name, or a device address).</summary>
        public string      TargetKey { get; }
        public string      Message   { get; }

        public DiagramOverlay(OverlayKind kind, string targetKey, string message)
        {
            Kind      = kind;
            TargetKey = targetKey;
            Message   = message;
        }
    }

    /// <summary>
    /// Overlay categories — extensible as new features are added.
    /// </summary>
    public enum OverlayKind
    {
        /// <summary>Validation warning or rule violation.</summary>
        Warning,
        /// <summary>Capacity gauge (e.g. loop current draw vs. limit).</summary>
        CapacityGauge,
        /// <summary>Informational note.</summary>
        Info,
    }
}
