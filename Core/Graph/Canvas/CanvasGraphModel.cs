using System;
using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Core.Graph.Canvas
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  CanvasGraphModel — internal topology graph ready for hybrid canvas
    //
    //  This model sits between ModuleData (raw collected data) and the
    //  presentation layer (TopologyViewModel / future Canvas renderer).
    //
    //  The TreeView is currently the only renderer; it reads from this model
    //  via TopologyViewModel's projection methods.  A future visual canvas
    //  will consume the same model to render draggable clusters and wires.
    //
    //  *** NO UI changes ***  — this is purely an internal data layer.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Root container for the hybrid canvas graph.
    /// Built once per refresh cycle from <see cref="ModuleData"/>.
    /// </summary>
    public sealed class CanvasGraphModel
    {
        /// <summary>Panel anchors (top-level system nodes).</summary>
        public IReadOnlyList<PanelAnchor> Panels { get; }

        /// <summary>Zone anchors (cross-cutting groupings).</summary>
        public IReadOnlyList<ZoneAnchor> Zones { get; }

        /// <summary>Global overlays (capacity warnings, summary counts).</summary>
        public IReadOnlyList<CanvasOverlay> Overlays { get; }

        /// <summary>Flat index of all graph nodes by Id for O(1) lookup.</summary>
        public IReadOnlyDictionary<string, Node> NodeIndex { get; }

        public CanvasGraphModel(
            IReadOnlyList<PanelAnchor> panels,
            IReadOnlyList<ZoneAnchor> zones,
            IReadOnlyList<CanvasOverlay> overlays,
            IReadOnlyDictionary<string, Node> nodeIndex)
        {
            Panels    = panels    ?? Array.Empty<PanelAnchor>();
            Zones     = zones     ?? Array.Empty<ZoneAnchor>();
            Overlays  = overlays  ?? Array.Empty<CanvasOverlay>();
            NodeIndex = nodeIndex ?? new Dictionary<string, Node>();
        }

        public static readonly CanvasGraphModel Empty =
            new CanvasGraphModel(null, null, null, null);
    }

    // ─── Anchors ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a fire-alarm control panel in the canvas graph.
    /// Contains child <see cref="LoopAnchor"/>s.
    /// </summary>
    public sealed class PanelAnchor
    {
        public string Id          { get; }
        public string DisplayName { get; }
        public double? Elevation  { get; }
        public int WarningCount   { get; }
        public IReadOnlyList<LoopAnchor> Loops { get; }

        public PanelAnchor(string id, string displayName, double? elevation,
                           int warningCount, IReadOnlyList<LoopAnchor> loops)
        {
            Id           = id;
            DisplayName  = displayName;
            Elevation    = elevation;
            WarningCount = warningCount;
            Loops        = loops ?? Array.Empty<LoopAnchor>();
        }
    }

    /// <summary>
    /// Represents a signalling loop in the canvas graph.
    /// Contains child <see cref="DeviceChip"/>s grouped into clusters.
    /// </summary>
    public sealed class LoopAnchor
    {
        public string Id          { get; }
        public string DisplayName { get; }
        public string ParentId    { get; }
        public int DeviceCount    { get; }
        public int WarningCount   { get; }
        public IReadOnlyList<DeviceCluster> Clusters { get; }

        public LoopAnchor(string id, string displayName, string parentId,
                          int deviceCount, int warningCount,
                          IReadOnlyList<DeviceCluster> clusters)
        {
            Id           = id;
            DisplayName  = displayName;
            ParentId     = parentId;
            DeviceCount  = deviceCount;
            WarningCount = warningCount;
            Clusters     = clusters ?? Array.Empty<DeviceCluster>();
        }
    }

    /// <summary>
    /// Represents a zone (cross-cutting grouping of devices).
    /// </summary>
    public sealed class ZoneAnchor
    {
        public string Id          { get; }
        public string DisplayName { get; }
        public int DeviceCount    { get; }
        public int WarningCount   { get; }

        public ZoneAnchor(string id, string displayName, int deviceCount, int warningCount)
        {
            Id = id; DisplayName = displayName;
            DeviceCount = deviceCount; WarningCount = warningCount;
        }
    }

    // ─── Clusters ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A group of devices at a particular elevation on a loop.
    /// In the TreeView this renders as flat children; on a future canvas
    /// these become stacked chips at a position.
    /// </summary>
    public sealed class DeviceCluster
    {
        public double? Elevation { get; }
        public IReadOnlyList<DeviceChip> Devices { get; }

        public DeviceCluster(double? elevation, IReadOnlyList<DeviceChip> devices)
        {
            Elevation = elevation;
            Devices   = devices ?? Array.Empty<DeviceChip>();
        }
    }

    /// <summary>
    /// A single addressable device in the graph.
    /// </summary>
    public sealed class DeviceChip
    {
        public string Id          { get; }
        public string Address     { get; }
        public string DeviceType  { get; }
        public long?  RevitId     { get; }
        public int    WarningCount { get; }

        public DeviceChip(string id, string address, string deviceType,
                          long? revitId, int warningCount)
        {
            Id = id; Address = address; DeviceType = deviceType;
            RevitId = revitId; WarningCount = warningCount;
        }
    }

    // ─── Overlays ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A canvas-level informational overlay (warning bubble, capacity gauge, etc.).
    /// </summary>
    public sealed class CanvasOverlay
    {
        public CanvasOverlayKind Kind      { get; }
        public string            TargetId  { get; }
        public string            Message   { get; }

        public CanvasOverlay(CanvasOverlayKind kind, string targetId, string message)
        { Kind = kind; TargetId = targetId; Message = message; }
    }

    public enum CanvasOverlayKind { Warning, CapacityGauge, Info }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Builder
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a <see cref="CanvasGraphModel"/> from <see cref="ModuleData"/>.
    /// Pure function — no side effects, no Revit dependency.
    /// </summary>
    public static class CanvasGraphBuilder
    {
        public static CanvasGraphModel Build(ModuleData data)
        {
            if (data == null) return CanvasGraphModel.Empty;

            // Index: nodeId → warning count
            var warningsByEntity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in data.RuleResults)
            {
                if (r.EntityId == null || r.Severity < Severity.Warning) continue;
                warningsByEntity[r.EntityId] =
                    warningsByEntity.TryGetValue(r.EntityId, out int c) ? c + 1 : 1;
            }

            int Warnings(string id) => warningsByEntity.TryGetValue(id ?? string.Empty, out int w) ? w : 0;

            // Build node index
            var nodeIndex = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in data.Nodes) nodeIndex[n.Id] = n;

            // Build adjacency: parent → children
            var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var hasParent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in data.Edges)
            {
                if (!children.TryGetValue(e.SourceId, out var list))
                    children[e.SourceId] = list = new List<string>();
                list.Add(e.TargetId);
                hasParent.Add(e.TargetId);
            }

            // Build device lookup by loop node id
            var devicesByLoop = data.Devices
                .GroupBy(d => d.LoopId ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Build panels
            var panels = new List<PanelAnchor>();
            foreach (var p in data.Panels)
            {
                var loopAnchors = new List<LoopAnchor>();
                var panelChildIds = children.TryGetValue(p.EntityId, out var pc) ? pc : new List<string>();
                foreach (var loopId in panelChildIds)
                {
                    if (!nodeIndex.TryGetValue(loopId, out var loopNode) || loopNode.NodeType != "Loop")
                        continue;

                    var devicesOnLoop = devicesByLoop.TryGetValue(loopId, out var dl) ? dl : new List<SystemModel.AddressableDevice>();

                    // Group devices by elevation for clusters
                    var clusters = devicesOnLoop
                        .GroupBy(d => d.Elevation.HasValue ? Math.Round(d.Elevation.Value, 3) : double.MinValue)
                        .OrderBy(g => g.Key)
                        .Select(g => new DeviceCluster(
                            g.Key == double.MinValue ? (double?)null : g.Key,
                            g.Select(d => new DeviceChip(
                                d.EntityId ?? string.Empty,
                                d.Address ?? string.Empty,
                                d.DeviceType ?? string.Empty,
                                d.RevitElementId,
                                Warnings(d.EntityId)
                            )).ToList()))
                        .ToList();

                    loopAnchors.Add(new LoopAnchor(
                        loopId, loopNode.Label, p.EntityId,
                        devicesOnLoop.Count, Warnings(loopId), clusters));
                }

                panels.Add(new PanelAnchor(
                    p.EntityId, p.DisplayName, p.Elevation,
                    Warnings(p.EntityId), loopAnchors));
            }

            // Build zones
            var zones = data.Zones
                .Select(z => new ZoneAnchor(
                    z.EntityId, z.DisplayName,
                    z.DeviceIds?.Count ?? 0, Warnings(z.EntityId)))
                .ToList();

            // Build global overlays (capacity warnings)
            var overlays = new List<CanvasOverlay>();
            foreach (var r in data.RuleResults)
            {
                if (r.Severity >= Severity.Warning && r.EntityId != null)
                {
                    overlays.Add(new CanvasOverlay(
                        r.Severity == Severity.Error ? CanvasOverlayKind.Warning : CanvasOverlayKind.Info,
                        r.EntityId, r.Message));
                }
            }

            return new CanvasGraphModel(panels, zones, overlays, nodeIndex);
        }
    }
}
