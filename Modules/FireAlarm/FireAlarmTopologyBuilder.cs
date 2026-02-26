using System;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Builds the topology graph for the Fire Alarm module.
    /// Produces a hierarchical graph: Panel -> Loop -> Device.
    /// Zones are optional and added when zone data is available.
    /// </summary>
    public class FireAlarmTopologyBuilder : ITopologyBuilder
    {
        public void Build(ModuleData data)
        {
            data.Nodes.Clear();
            data.Edges.Clear();

            // Create panel nodes
            foreach (Panel panel in data.Panels)
            {
                var node = new Node(panel.EntityId, panel.DisplayName, "Panel")
                {
                    RevitElementId = panel.RevitElementId,
                };
                data.Nodes.Add(node);
            }

            // Create loop nodes and connect to panels
            foreach (Loop loop in data.Loops)
            {
                var node = new Node(loop.EntityId, loop.DisplayName, "Loop")
                {
                    RevitElementId = loop.RevitElementId,
                };
                node.Properties["DeviceCount"] = loop.Devices.Count.ToString();
                data.Nodes.Add(node);

                if (!string.IsNullOrEmpty(loop.PanelId))
                {
                    data.Edges.Add(new Edge(loop.PanelId, loop.EntityId, "contains"));
                }
            }

            // Create zone nodes (if any)
            foreach (Zone zone in data.Zones)
            {
                var node = new Node(zone.EntityId, zone.DisplayName, "Zone");
                data.Nodes.Add(node);

                // Connect zone devices
                foreach (string deviceId in zone.DeviceIds)
                {
                    data.Edges.Add(new Edge(zone.EntityId, deviceId, "includes"));
                }
            }

            // Create device nodes and connect to loops
            foreach (AddressableDevice device in data.Devices)
            {
                var node = new Node(device.EntityId, device.DisplayName, "Device")
                {
                    RevitElementId = device.RevitElementId,
                };


                // ── Pinned top-7 properties (inserted first to control display order) ──

                // 1. "Name" = device type label.
                if (!string.IsNullOrEmpty(device.DeviceType))
                    node.Properties["Name"] = device.DeviceType;
                else if (device.Properties.TryGetValue("_Name", out string rawName))
                    node.Properties["Name"] = rawName;

                // 2. "Level" = Revit level name the device is placed on.
                if (device.Properties.TryGetValue("_LevelName", out string levelName)
                    && !string.IsNullOrEmpty(levelName))
                    node.Properties["Level"] = levelName;

                // 3. "Elevation" = offset from level, converted from feet to metres.
                if (device.Elevation.HasValue)
                {
                    double metres = device.Elevation.Value * 0.3048;
                    node.Properties["Elevation"] = metres.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture) + " m";
                }

                // 4. "Panel" = the panel display name the device is assigned to.
                Panel panel = null;
                if (!string.IsNullOrEmpty(device.PanelId))
                    panel = data.Panels.Find(p => p.EntityId == device.PanelId);
                if (panel != null)
                    node.Properties["Panel"] = panel.DisplayName;

                // 5. "Panel type" = the PanelConfig value from the device's own param (written by topology assignments).
                device.Properties.TryGetValue("_PanelConfig", out string panelConfig);
                node.Properties["Panel type"] = panelConfig ?? string.Empty;

                // 6. "Loop" = the loop number/label the device belongs to.
                if (device.Properties.TryGetValue("_LoopValue", out string loopValue)
                    && !string.IsNullOrEmpty(loopValue))
                    node.Properties["Loop"] = loopValue;

                // 7. "Address" = device address within the loop.
                if (!string.IsNullOrEmpty(device.Address))
                    node.Properties["Address"] = device.Address;


                // ── Remaining Revit parameters ──
                // Skip internal "_" keys and any already-pinned keys above.
                foreach (var kvp in device.Properties)
                {
                    if (kvp.Key.StartsWith("_"))
                        continue;
                    if (string.Equals(kvp.Key, "DeviceType", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!node.Properties.ContainsKey(kvp.Key))
                        node.Properties[kvp.Key] = kvp.Value;
                }

                // Emit relabeled semantic properties after the bulk copy.
                device.Properties.TryGetValue("_CurrentDrawNormal", out string currentDrawNormal);
                device.Properties.TryGetValue("_CurrentDrawAlarm", out string currentDrawAlarm);
                node.Properties["Current draw normal"] = currentDrawNormal ?? string.Empty;
                node.Properties["Current draw alarm"] = currentDrawAlarm ?? string.Empty;
                if (device.Properties.TryGetValue("_Wire", out string wire) && !string.IsNullOrEmpty(wire))
                    node.Properties["Wire"] = wire;
                if (device.Properties.TryGetValue("_LoopModuleConfig", out string loopModule) && !string.IsNullOrEmpty(loopModule))
                    node.Properties["Loop module"] = loopModule;

                data.Nodes.Add(node);

                if (!string.IsNullOrEmpty(device.LoopId))
                {
                    data.Edges.Add(new Edge(device.LoopId, device.EntityId, "contains"));
                }
            }
        }
    }
}
