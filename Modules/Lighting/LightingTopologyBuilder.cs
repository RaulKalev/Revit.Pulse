using System;
using System.Collections.Generic;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Builds the topology graph for the Lighting module.
    /// Produces a hierarchical graph: Controller → Line → Device.
    /// Zones are optional and added when zone data is available.
    ///
    /// This mirrors Fire Alarm's Panel → Loop → Device pattern but with
    /// lighting-specific terminology.
    /// </summary>
    public class LightingTopologyBuilder : ITopologyBuilder
    {
        public void Build(ModuleData data)
        {
            data.Nodes.Clear();
            data.Edges.Clear();

            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return;

            // Create controller nodes (equivalent to "Panel" nodes in FA)
            foreach (Panel controller in lg.Controllers)
            {
                var node = new Node(controller.EntityId, controller.DisplayName, "Panel")
                {
                    RevitElementId = controller.RevitElementId,
                };
                if (controller.Elevation.HasValue)
                    node.Properties["_ElevationFt"] = controller.Elevation.Value.ToString(
                        "R", System.Globalization.CultureInfo.InvariantCulture);
                data.Nodes.Add(node);
            }

            // Create line nodes (equivalent to "Loop" nodes in FA) and connect to controllers
            foreach (Loop line in lg.Lines)
            {
                var node = new Node(line.EntityId, line.DisplayName, "Loop")
                {
                    RevitElementId = line.RevitElementId,
                };
                node.Properties["DeviceCount"] = line.Devices.Count.ToString();

                data.Nodes.Add(node);

                if (!string.IsNullOrEmpty(line.PanelId))
                {
                    data.Edges.Add(new Edge(line.PanelId, line.EntityId, "contains"));
                }
            }

            // Create zone nodes (if any)
            foreach (Zone zone in lg.Zones)
            {
                var node = new Node(zone.EntityId, zone.DisplayName, "Zone");
                data.Nodes.Add(node);

                foreach (string deviceId in zone.DeviceIds)
                {
                    data.Edges.Add(new Edge(zone.EntityId, deviceId, "includes"));
                }
            }

            // Create device nodes and connect to lines
            foreach (AddressableDevice device in lg.Devices)
            {
                var node = new Node(device.EntityId, device.DisplayName, "Device")
                {
                    RevitElementId = device.RevitElementId,
                };

                // ── Pinned properties (inserted first to control display order) ──

                // 1. "Name" = device type label
                if (!string.IsNullOrEmpty(device.DeviceType))
                    node.Properties["Name"] = device.DeviceType;
                else if (device.Properties.TryGetValue("_Name", out string rawName))
                    node.Properties["Name"] = rawName;

                // 2. "Level" = Revit level name
                if (device.Properties.TryGetValue("_LevelName", out string levelName)
                    && !string.IsNullOrEmpty(levelName))
                    node.Properties["Level"] = levelName;

                // 3. "Elevation"
                if (device.Elevation.HasValue)
                {
                    double metres = device.Elevation.Value * 0.3048;
                    node.Properties["Elevation"] = metres.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture) + " m";
                }

                // Write raw elevation and line ID for CanvasGraphBuilder
                if (device.Elevation.HasValue)
                    node.Properties["_ElevationRaw"] = device.Elevation.Value.ToString(
                        "R", System.Globalization.CultureInfo.InvariantCulture);
                node.Properties["_LoopId"] = device.LoopId ?? string.Empty;

                // 4. "Controller" = the controller display name
                Panel ctrl = null;
                if (!string.IsNullOrEmpty(device.PanelId))
                    ctrl = lg.Controllers.Find(c => c.EntityId == device.PanelId);
                if (ctrl != null)
                    node.Properties["Controller"] = ctrl.DisplayName;

                // 5. "Line" = the line number/label
                if (device.Properties.TryGetValue("_LineValue", out string lineValue)
                    && !string.IsNullOrEmpty(lineValue))
                    node.Properties["Line"] = lineValue;

                // 6. "Address" = device address within the line
                if (!string.IsNullOrEmpty(device.Address))
                    node.Properties["Address"] = device.Address;

                // ── Remaining Revit parameters ──
                foreach (var kvp in device.Properties)
                {
                    if (kvp.Key.StartsWith("_"))
                        continue;
                    if (string.Equals(kvp.Key, "DeviceType", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!node.Properties.ContainsKey(kvp.Key))
                        node.Properties[kvp.Key] = kvp.Value;
                }

                // Emit relabeled semantic properties
                device.Properties.TryGetValue("_CurrentDraw", out string currentDraw);
                if (!string.IsNullOrEmpty(currentDraw))
                    node.Properties["Current draw (mA)"] = currentDraw;
                if (device.Properties.TryGetValue("_SystemType", out string sysType) && !string.IsNullOrEmpty(sysType))
                    node.Properties["System"] = sysType;

                data.Nodes.Add(node);

                // Connect device to its line
                if (!string.IsNullOrEmpty(device.LoopId))
                    data.Edges.Add(new Edge(device.LoopId, device.EntityId, "contains"));
            }
        }
    }
}
