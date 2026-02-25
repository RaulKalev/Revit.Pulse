using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Graph.Canvas;
using Pulse.Core.Modules;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Builds a <see cref="DiagramScene"/> snapshot from the current state of a
    /// <see cref="DiagramViewModel"/>.  The resulting scene is a pure-data description
    /// of what the canvas should display — no pixel positions, no WPF types.
    ///
    /// Call <see cref="Build"/> after every data mutation that should be reflected
    /// on the canvas (LoadPanels, flip, wire change, etc.).
    /// </summary>
    public static class DiagramSceneBuilder
    {
        /// <summary>
        /// Create a complete <see cref="DiagramScene"/> from the view-model's current state.
        /// </summary>
        public static DiagramScene Build(DiagramViewModel vm)
        {
            if (vm == null) return DiagramScene.Empty;

            var levels   = BuildLevels(vm);
            var panels   = BuildPanels(vm);
            var overlays = BuildOverlays(vm);

            return new DiagramScene(levels, panels, overlays);
        }

        // ─── Levels ──────────────────────────────────────────────────────

        private static List<LevelAnchor> BuildLevels(DiagramViewModel vm)
        {
            var sorted = vm.Levels.OrderBy(l => l.Elevation).ToList();
            var anchors = new List<LevelAnchor>(sorted.Count);

            for (int i = 0; i < sorted.Count; i++)
            {
                var level     = sorted[i];
                string above  = level.Name;
                string below  = (i > 0) ? sorted[i - 1].Name : null;

                bool lineVis      = vm.GetLineState(level.Name) == LevelState.Visible;
                bool textAboveVis = vm.GetTextAboveState(level.Name) == LevelState.Visible;
                bool textBelowVis = below != null
                                    && vm.GetTextBelowState(level.Name) == LevelState.Visible;

                anchors.Add(new LevelAnchor(
                    level.Name, level.Elevation,
                    lineVis, textAboveVis, textBelowVis,
                    above, below));
            }

            return anchors;
        }

        // ─── Panels / Loops / Devices ────────────────────────────────────

        private static List<PanelCluster> BuildPanels(DiagramViewModel vm)
        {
            var clusters = new List<PanelCluster>();

            foreach (var p in vm.Panels)
            {
                var loops = new List<LoopCluster>();
                int naturalIndex = 0;

                foreach (var loop in p.LoopInfos)
                {
                    string key       = p.Name + "::" + loop.Name;
                    bool   flipped   = vm.IsLoopFlipped(p.Name, loop.Name);
                    int    wireCount = vm.GetLoopWireCount(p.Name, loop.Name);
                    int    rank      = vm.GetLoopRank(p.Name, loop.Name, naturalIndex);
                    string wireColor = vm.GetLoopWireColor(p.Name, loop.Name);

                    // Per-elevation device rows
                    var rows = loop.Levels
                        .Select(lv => new DeviceRow(lv.Elevation, lv.DeviceCount, lv.TypeCounts))
                        .ToList();

                    // Flat device list ordered by address
                    var devices = new List<DeviceSlot>(loop.DeviceTypesByAddress.Count);
                    for (int i = 0; i < loop.DeviceTypesByAddress.Count; i++)
                    {
                        string dt   = loop.DeviceTypesByAddress[i];
                        string addr = (i < loop.DeviceAddresses.Count)
                                        ? loop.DeviceAddresses[i]
                                        : string.Empty;
                        string sym  = vm.GetDeviceTypeSymbol(dt);
                        devices.Add(new DeviceSlot(addr, dt, sym));
                    }

                    loops.Add(new LoopCluster(
                        loop.Name, key, flipped,
                        wireCount, rank, wireColor,
                        rows, devices));

                    naturalIndex++;
                }

                clusters.Add(new PanelCluster(
                    p.Name, p.Elevation, p.ConfigLoopCount, loops));
            }

            return clusters;
        }

        // ─── Overlays (placeholder — populated by future rule engines) ───

        private static List<DiagramOverlay> BuildOverlays(DiagramViewModel vm)
        {
            // No overlays produced yet.
            // Future steps will feed validation-rule warnings,
            // capacity-gauge results, and info annotations here.
            return new List<DiagramOverlay>();
        }
    }
}
