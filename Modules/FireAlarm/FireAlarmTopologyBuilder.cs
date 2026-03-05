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

            // ── SubCircuit projection ─────────────────────────────────────────────────
            // SubCircuits are virtual grouping nodes attached to host Output Module devices.
            // They do NOT re-parent loop devices — they add a summary child under the host.
            if (data.SubCircuits != null && data.SubCircuits.Count > 0)
                BuildSubCircuitNodes(data);
        }

        /// <summary>
        /// Project SubCircuits from <see cref="ModuleData.SubCircuits"/> into the graph.
        ///
        /// Each SubCircuit becomes a <c>"SubCircuit"</c> node that is attached to its host
        /// device node via a <c>"hosts"</c> edge.  Device nodes that belong to the SubCircuit
        /// are NOT re-parented — they remain children of their loop — but the SubCircuit node
        /// stores aggregate Properties (DeviceCount, TotalMa) for UI display and rule evaluation.
        /// </summary>
        private static void BuildSubCircuitNodes(ModuleData data)
        {
            // Build a fast map: RevitElementId → Node (for host resolution + mA lookup)
            var nodeByElementId = new Dictionary<long, Node>();
            foreach (var node in data.Nodes)
                if (node.RevitElementId.HasValue)
                    nodeByElementId[node.RevitElementId.Value] = node;

            // Build a fast map: RevitElementId → AddressableDevice (for mA aggregation)
            var deviceByElementId = new Dictionary<long, AddressableDevice>();
            foreach (var dev in data.Devices)
                if (dev.RevitElementId.HasValue)
                    deviceByElementId[dev.RevitElementId.Value] = dev;

            // Build a reverse map: sub-device node id → parent Device node id.
            // Used so a SubCircuit whose HostElementId is a sub-device ("001.1") is
            // displayed as a child of the Output Module ("001"), not the sub-device.
            // The XAML only renders one level of Device children, so the SubCircuit must
            // attach to the Output Module for it to be visible.
            var nodeById = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var n in data.Nodes)
                nodeById[n.Id] = n;

            var parentDeviceOfNode = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var edge in data.Edges)
            {
                if (nodeById.TryGetValue(edge.SourceId, out var src)
                    && nodeById.TryGetValue(edge.TargetId, out var tgt)
                    && src.NodeType == "Device"
                    && tgt.NodeType == "Device")
                {
                    parentDeviceOfNode[edge.TargetId] = edge.SourceId;
                }
            }

            foreach (var sc in data.SubCircuits)
            {
                if (string.IsNullOrEmpty(sc.Id)) continue;

                // ── Aggregate device metrics ──────────────────────────────────────────
                int deviceCount  = 0;
                double totalMaNormal = 0;
                double totalMaAlarm  = 0;
                bool hasMaData       = false;

                foreach (int elemId in sc.DeviceElementIds)
                {
                    deviceCount++;
                    if (deviceByElementId.TryGetValue(elemId, out var dev)
                        && dev.CurrentDraw.HasValue)
                    {
                        totalMaNormal += dev.CurrentDraw.Value;
                        hasMaData = true;
                        // Alarm draw: read from raw property, fall back to normal draw
                        if (dev.Properties.TryGetValue("_CurrentDrawAlarm", out string alarmStr)
                            && double.TryParse(alarmStr,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double alarmMa))
                            totalMaAlarm += alarmMa;
                        else
                            totalMaAlarm += dev.CurrentDraw.Value;
                    }
                }

                // ── Cable length estimate (host → devices, Manhattan routing) ──────────
                double cableLengthFeet = 0;
                bool hasCableRoute = false;
                double prevX = 0, prevY = 0, prevZ = 0;

                // Per-device cumulative distances from the host (feet), stored as
                // "elemId:distFeet" CSV so MetricsPanelViewModel can do distributed V-drop.
                // Also store alarm mA per device for the same purpose.
                var deviceDistPairs  = new System.Text.StringBuilder();
                var deviceMaPairs    = new System.Text.StringBuilder();

                if (deviceByElementId.TryGetValue(sc.HostElementId, out var hostDevLen)
                    && hostDevLen.LocationX.HasValue
                    && hostDevLen.LocationY.HasValue
                    && hostDevLen.LocationZ.HasValue)
                {
                    prevX = hostDevLen.LocationX.Value;
                    prevY = hostDevLen.LocationY.Value;
                    prevZ = hostDevLen.LocationZ.Value;
                    double runningFeet = 0;

                    foreach (int elemId in sc.DeviceElementIds)
                    {
                        if (deviceByElementId.TryGetValue(elemId, out var lenDev)
                            && lenDev.LocationX.HasValue
                            && lenDev.LocationY.HasValue
                            && lenDev.LocationZ.HasValue)
                        {
                            double segFeet = Math.Abs(lenDev.LocationX.Value - prevX)
                                           + Math.Abs(lenDev.LocationY.Value - prevY)
                                           + Math.Abs(lenDev.LocationZ.Value - prevZ);
                            cableLengthFeet += segFeet;
                            runningFeet     += segFeet;
                            prevX = lenDev.LocationX.Value;
                            prevY = lenDev.LocationY.Value;
                            prevZ = lenDev.LocationZ.Value;
                            hasCableRoute = true;

                            // Cumulative distance entry
                            if (deviceDistPairs.Length > 0) deviceDistPairs.Append(',');
                            deviceDistPairs.Append(elemId);
                            deviceDistPairs.Append(':');
                            deviceDistPairs.Append(runningFeet.ToString(
                                "F4", System.Globalization.CultureInfo.InvariantCulture));

                            // Alarm mA entry
                            double alarmMaEntry = 0;
                            if (deviceByElementId.TryGetValue(elemId, out var maDev))
                            {
                                double normalMaEntry = maDev.CurrentDraw ?? 0;
                                alarmMaEntry = normalMaEntry;
                                if (maDev.Properties.TryGetValue("_CurrentDrawAlarm", out string aStr)
                                    && double.TryParse(aStr,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double parsedA))
                                    alarmMaEntry = parsedA;
                            }
                            if (deviceMaPairs.Length > 0) deviceMaPairs.Append(',');
                            deviceMaPairs.Append(elemId);
                            deviceMaPairs.Append(':');
                            deviceMaPairs.Append(alarmMaEntry.ToString(
                                "F4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                }

                // ── Create SubCircuit node ────────────────────────────────────────────
                var scNode = new Node(
                    id:       "subcircuit::" + sc.Id,
                    label:    sc.Name ?? sc.Id,
                    nodeType: "SubCircuit")
                {
                    // No RevitElementId — this is a virtual node
                };

                scNode.Properties["DeviceCount"] = deviceCount.ToString();
                scNode.Properties["HostElementId"] = sc.HostElementId.ToString();
                if (!string.IsNullOrEmpty(sc.WireTypeKey))
                    scNode.Properties["WireType"] = sc.WireTypeKey;
                // Prefer summed route lengths from tagged 3D lines when available.
                if (data.CableRouteLengths.TryGetValue(sc.HostElementId, out double routeMetres)
                    && routeMetres > 0)
                {
                    scNode.Properties["CableLength"] = routeMetres.ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (hasCableRoute)
                    scNode.Properties["CableLength"] = (cableLengthFeet * 0.3048).ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
                if (hasMaData)
                {
                    scNode.Properties["TotalMaNormal"] = totalMaNormal.ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
                    scNode.Properties["TotalMaAlarm"] = totalMaAlarm.ToString(
                        "F1", System.Globalization.CultureInfo.InvariantCulture);
                }

                // Distributed V-drop data: cumulative distances + per-device alarm mA
                if (deviceDistPairs.Length > 0)
                    scNode.Properties["DeviceCumulativeDistFeet"] = deviceDistPairs.ToString();
                if (deviceMaPairs.Length > 0)
                    scNode.Properties["DeviceAlarmMa"] = deviceMaPairs.ToString();

                // Nominal voltage from host device (if mapped parameter is populated)
                if (deviceByElementId.TryGetValue(sc.HostElementId, out var hostDevNomV)
                    && hostDevNomV.Properties.TryGetValue("_NominalVoltage", out string scNomVoltStr)
                    && !string.IsNullOrEmpty(scNomVoltStr))
                {
                    scNode.Properties["NominalVoltage"] = scNomVoltStr;
                }

                data.Nodes.Add(scNode);

                // ── Edge: host device → SubCircuit ────────────────────────────────────
                // If the host is a sub-device ("001.1"), attach to its parent Output Module
                // ("001") instead so the SubCircuit appears at the correct XAML render level
                // (Device children, not Device-grandchildren which are never rendered).
                // HostElementId is still the sub-device — used for routing coordinates.
                if (nodeByElementId.TryGetValue(sc.HostElementId, out var hostNode))
                {
                    string edgeSourceId = parentDeviceOfNode.TryGetValue(hostNode.Id, out string parentId)
                        ? parentId
                        : hostNode.Id;
                    data.Edges.Add(new Edge(edgeSourceId, scNode.Id, "hosts"));
                }
                // If host is deleted/missing, SubCircuit node is added as a root orphan.
                // Rule engine can flag it; UI will show it under "(No Host)".

                // ── Member reference nodes (one per assigned device) ───────────────────
                // These are virtual display-only nodes so devices appear listed under
                // the SubCircuit card in the topology panel without being re-parented.
                foreach (int elemId in sc.DeviceElementIds)
                {
                    string memberLabel = deviceByElementId.TryGetValue(elemId, out var memberDev)
                        ? (memberDev.DisplayName ?? elemId.ToString())
                        : elemId.ToString();

                    var memberNode = new Node(
                        id:       "scmember::" + sc.Id + "::" + elemId,
                        label:    memberLabel,
                        nodeType: "SubCircuitMember");

                    memberNode.Properties["SubCircuitId"]    = sc.Id;
                    memberNode.Properties["MemberElementId"] = elemId.ToString();

                    if (deviceByElementId.TryGetValue(elemId, out var mDev))
                    {
                        if (mDev.CurrentDraw.HasValue)
                        {
                            memberNode.Properties["CurrentDraw"] =
                                mDev.CurrentDraw.Value.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture) + " mA";

                            // Alarm draw: use device-specific property if available, else fall back to normal
                            double alarmMa = mDev.CurrentDraw.Value;
                            if (mDev.Properties.TryGetValue("_CurrentDrawAlarm", out string alarmStr)
                                && double.TryParse(alarmStr,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double parsedAlarm))
                                alarmMa = parsedAlarm;
                            memberNode.Properties["CurrentDrawAlarm"] =
                                alarmMa.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture) + " mA";
                        }
                    }

                    data.Nodes.Add(memberNode);
                    data.Edges.Add(new Edge(scNode.Id, memberNode.Id, "member"));
                }
            }
        }
    }
}
