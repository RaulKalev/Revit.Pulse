using System;
using System.Collections.Generic;
using System.Linq;
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
            // Pre-calculate cable lengths for all loops (keyed by loop EntityId).
            var cableLengths = CableLengthCalculator.CalculateAll(data.Panels);

            foreach (Loop loop in data.Loops)
            {
                var node = new Node(loop.EntityId, loop.DisplayName, "Loop")
                {
                    RevitElementId = loop.RevitElementId,
                };
                node.Properties["DeviceCount"] = loop.Devices.Count.ToString();

                // Attach cable length to the node so the UI can show it.
                if (cableLengths.TryGetValue(loop.EntityId, out var cableResult)
                    && cableResult.RoutedDeviceCount > 0)
                {
                    node.Properties["CableLength"] = cableResult.TotalLengthMetres.ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
                }

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
            }

            // Build lookups for sub-device reparenting.
            // Panel is often a read-only circuit-derived param that cannot be written,
            // so the sub-device may land in a different LoopId bucket than its host.
            // We therefore build three fallback tiers:
            //   1. loopId|address          — exact (panel+loop+address)
            //   2. loopValue|address       — loop value + address, ignoring panel (most useful fallback)
            //   3. address                 — address only, preferring real-panel devices
            var deviceByLoopAndAddress      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var deviceByLoopValueAndAddress = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var deviceByAddressOnly         = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in data.Devices)
            {
                if (!string.IsNullOrEmpty(device.LoopId) && !string.IsNullOrEmpty(device.Address))
                    deviceByLoopAndAddress[device.LoopId + "|" + device.Address] = device.EntityId;
                if (!string.IsNullOrEmpty(device.Address))
                {
                    // Tier-2 key: raw loop value (e.g. "1") stored by the collector as "_LoopValue"
                    device.Properties.TryGetValue("_LoopValue", out string rawLoopVal);
                    string lvKey = (rawLoopVal ?? string.Empty) + "|" + device.Address;
                    if (!deviceByLoopValueAndAddress.TryGetValue(lvKey, out var lvList))
                        deviceByLoopValueAndAddress[lvKey] = lvList = new List<string>();
                    lvList.Add(device.EntityId);

                    // Tier-3 key: address only
                    if (!deviceByAddressOnly.TryGetValue(device.Address, out var addrList))
                        deviceByAddressOnly[device.Address] = addrList = new List<string>();
                    addrList.Add(device.EntityId);
                }
            }

            // Emit edges: dotted-address devices (e.g. "001.1") become children of
            // the base-address device ("001") in the same loop; others connect to loop.
            foreach (var device in data.Devices)
            {
                if (string.IsNullOrEmpty(device.LoopId)) continue;

                string parentId = null;
                if (!string.IsNullOrEmpty(device.Address))
                {
                    int lastDot = device.Address.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        string parentAddress = device.Address.Substring(0, lastDot);

                        // Tier 1 — exact loopId + address
                        if (!deviceByLoopAndAddress.TryGetValue(device.LoopId + "|" + parentAddress, out parentId))
                        {
                            // Tier 2 — same loop value (e.g. "1") + address, ignoring panel.
                            // This handles the common case where Panel is circuit-derived/read-only
                            // so the sub-device ends up in "(No Panel)::1" while the host is in
                            // "ATS panel::1" — both share loop value "1".
                            device.Properties.TryGetValue("_LoopValue", out string subLoopVal);
                            string tier2Key = (subLoopVal ?? string.Empty) + "|" + parentAddress;
                            if (deviceByLoopValueAndAddress.TryGetValue(tier2Key, out var tier2Candidates))
                            {
                                // Pick the candidate in a real panel (not "(No Panel)") over placeholders
                                foreach (var cId in tier2Candidates)
                                {
                                    if (cId == device.EntityId) continue; // skip self
                                    var cand = data.Devices.FirstOrDefault(d => d.EntityId == cId);
                                    if (cand != null
                                        && !string.IsNullOrEmpty(cand.LoopId)
                                        && !cand.LoopId.Contains("(No Panel)")
                                        && !cand.LoopId.Contains("(No Loop)"))
                                    {
                                        parentId = cId;
                                        break;
                                    }
                                }
                                // Fallback within tier 2: take first non-self
                                if (parentId == null)
                                    parentId = tier2Candidates.FirstOrDefault(id => id != device.EntityId);
                            }

                            // Tier 3 — address only, prefer real-panel, as last resort
                            if (parentId == null
                                && deviceByAddressOnly.TryGetValue(parentAddress, out var tier3Candidates))
                            {
                                foreach (var cId in tier3Candidates)
                                {
                                    if (cId == device.EntityId) continue;
                                    var cand = data.Devices.FirstOrDefault(d => d.EntityId == cId);
                                    if (cand != null
                                        && !string.IsNullOrEmpty(cand.LoopId)
                                        && !cand.LoopId.Contains("(No Panel)")
                                        && !cand.LoopId.Contains("(No Loop)"))
                                    {
                                        parentId = cId;
                                        break;
                                    }
                                }
                                if (parentId == null)
                                    parentId = tier3Candidates.FirstOrDefault(id => id != device.EntityId);
                            }
                        }
                    }
                }

                string sourceId = parentId ?? device.LoopId;
                data.Edges.Add(new Edge(sourceId, device.EntityId, "contains"));
            }
        }
    }
}
