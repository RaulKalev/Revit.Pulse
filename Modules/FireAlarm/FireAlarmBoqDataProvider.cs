using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pulse.Core.Boq;
using Pulse.Core.Modules;
using Pulse.Core.Modules.Metrics;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Converts a <see cref="ModuleData"/> snapshot produced by the FireAlarm module
    /// into a flat list of <see cref="BoqItem"/> rows for the BOQ DataGrid.
    ///
    /// Emits five row groups in order:
    ///   1. Fire alarm devices (one row each)
    ///   2. Control panels    (one row per assigned panel config)
    ///   3. Loop modules      (one row per assigned loop module config)
    ///   4. Cables            (one row per wire type, length summed across loops)
    ///   5. Batteries         (one row per FACP, one per field PSU group)
    /// </summary>
    public class FireAlarmBoqDataProvider : IBoqDataProvider
    {
        private readonly TopologyAssignmentsStore _assignments;
        private readonly DeviceConfigStore        _deviceStore;

        public FireAlarmBoqDataProvider(
            TopologyAssignmentsStore assignments,
            DeviceConfigStore        deviceStore)
        {
            _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            _deviceStore = deviceStore  ?? throw new ArgumentNullException(nameof(deviceStore));
        }

        public string ModuleKey => "FireAlarm";

        // ── IBoqDataProvider ─────────────────────────────────────────────────

        public IReadOnlyList<BoqItem> GetItems(ModuleData data)
        {
            if (data == null) return Array.Empty<BoqItem>();
            var fa = data.GetPayload<FireAlarmPayload>();
            if (fa == null) return Array.Empty<BoqItem>();

            var faDevConfig = DeviceConfigService.LoadModuleConfig<FireAlarmDeviceConfig>(_deviceStore, "FireAlarm");

            // Build fast lookup maps from the topology.
            var loopById      = fa.Loops.ToDictionary(l => l.EntityId, l => l, StringComparer.OrdinalIgnoreCase);
            var panelById     = fa.Panels.ToDictionary(p => p.EntityId, p => p, StringComparer.OrdinalIgnoreCase);
            var panelByLoopId = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in fa.Panels)
                foreach (var l in p.Loops)
                    panelByLoopId[l.EntityId] = p;

            var items = new List<BoqItem>();

            // ── 1. Fire alarm devices ─────────────────────────────────────────
            foreach (var device in fa.Devices)
            {
                device.Properties.TryGetValue("_CategoryName", out string revitCategory);
                var item = new BoqItem
                {
                    ElementId = device.RevitElementId,
                    Level     = device.LevelName ?? string.Empty,
                    Category  = revitCategory ?? device.DeviceType ?? string.Empty,
                };

                device.Properties.TryGetValue("_FamilyName", out string familyName);
                device.Properties.TryGetValue("_Name",       out string typeName);
                item.FamilyName = familyName ?? string.Empty;
                item.TypeName   = typeName   ?? string.Empty;

                if (!string.IsNullOrEmpty(device.LoopId)
                    && loopById.TryGetValue(device.LoopId, out var devLoop))
                    item.Loop = devLoop.DisplayName ?? device.LoopId;

                if (!string.IsNullOrEmpty(device.PanelId)
                    && panelById.TryGetValue(device.PanelId, out var devPanel))
                    item.Panel = devPanel.DisplayName ?? device.PanelId;

                foreach (var kvp in device.Properties)
                {
                    if (kvp.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    item.Parameters[kvp.Key] = kvp.Value;
                }

                if (!string.IsNullOrEmpty(device.Address))
                    item.Parameters["Address"] = device.Address;
                if (device.CurrentDraw.HasValue)
                    item.Parameters["CurrentDraw_mA"] = device.CurrentDraw.Value.ToString(CultureInfo.InvariantCulture);

                items.Add(item);
            }

            // ── 2. Control panels ─────────────────────────────────────────────
            foreach (var panel in fa.Panels)
            {
                if (!_assignments.PanelAssignments.TryGetValue(panel.DisplayName, out string cfgName)
                    || string.IsNullOrEmpty(cfgName))
                    continue;

                var cfg = _deviceStore.ControlPanels.FirstOrDefault(
                    c => string.Equals(c.Name, cfgName, StringComparison.OrdinalIgnoreCase));
                if (cfg == null) continue;

                var item = new BoqItem
                {
                    Category   = "Control Panels",
                    FamilyName = cfg.Name,
                    TypeName   = "Fire Alarm Control Panel",
                    Panel      = panel.DisplayName,
                };
                item.Parameters["Quantity"]     = "1";
                item.Parameters["MaxAddresses"] = (cfg.MaxAddresses > 0
                    ? cfg.MaxAddresses
                    : cfg.AddressesPerLoop * Math.Max(panel.Loops.Count, 1)).ToString(CultureInfo.InvariantCulture);
                item.Parameters["MaxLoopCount"] = cfg.MaxLoopCount.ToString(CultureInfo.InvariantCulture);
                item.Parameters["MaxMaPerLoop"] = cfg.MaxMaPerLoop.ToString(CultureInfo.InvariantCulture);
                items.Add(item);
            }

            // ── 3. Loop modules ───────────────────────────────────────────────
            foreach (var loop in fa.Loops)
            {
                if (!_assignments.LoopAssignments.TryGetValue(loop.DisplayName, out string modName)
                    || string.IsNullOrEmpty(modName))
                    continue;

                var mod = _deviceStore.LoopModules.FirstOrDefault(
                    m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
                if (mod == null) continue;

                string parentPanel = panelByLoopId.TryGetValue(loop.EntityId, out var lp)
                    ? lp.DisplayName : string.Empty;

                var item = new BoqItem
                {
                    Category   = "Loop Modules",
                    FamilyName = mod.Name,
                    TypeName   = "Loop Module",
                    Panel      = parentPanel,
                    Loop       = loop.DisplayName,
                };
                item.Parameters["Quantity"]        = "1";
                item.Parameters["AddressesPerLoop"] = mod.AddressesPerLoop.ToString(CultureInfo.InvariantCulture);
                item.Parameters["MaxMaPerLoop"]     = mod.MaxMaPerLoop.ToString(CultureInfo.InvariantCulture);
                items.Add(item);
            }

            // ── 4. Cables — one row per wire type, lengths summed ─────────────
            // Accumulate total length and panel list per wire config name.
            var wireLength = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var panel in fa.Panels)
            {
                foreach (var loop in panel.Loops)
                {
                    string wKey = $"{panel.DisplayName}::{loop.DisplayName}";
                    if (!_assignments.LoopWireAssignments.TryGetValue(wKey, out string wireName)
                        || string.IsNullOrEmpty(wireName))
                        continue;

                    double lengthM = 0.0;
                    try
                    {
                        var cable = CableLengthCalculator.Calculate(loop, panel);
                        lengthM = cable.TotalLengthMetres;
                    }
                    catch { /* length unavailable — still emit the row with 0 */ }

                    if (wireLength.ContainsKey(wireName))
                        wireLength[wireName] += lengthM;
                    else
                        wireLength[wireName] = lengthM;
                }
            }

            foreach (var kvp in wireLength)
            {
                string wireName = kvp.Key;
                double totalM   = kvp.Value;

                WireConfig wireCfg = null;
                if (faDevConfig != null)
                    wireCfg = faDevConfig.Wires.FirstOrDefault(
                        w => string.Equals(w.Name, wireName, StringComparison.OrdinalIgnoreCase));

                string typeLabel = wireCfg != null
                    ? $"{wireCfg.CoreCount}\u00d7 {wireCfg.CoreSizeMm2:F1} mm\u00b2"
                    : wireName;

                var item = new BoqItem
                {
                    Category   = "Cables",
                    FamilyName = wireName,
                    TypeName   = typeLabel,
                };
                item.Parameters["Quantity"]     = totalM.ToString("F1", CultureInfo.InvariantCulture) + " m";
                item.Parameters["CableLength_m"] = totalM.ToString("F1", CultureInfo.InvariantCulture);
                if (wireCfg != null)
                {
                    item.Parameters["CoreCount"]      = wireCfg.CoreCount.ToString(CultureInfo.InvariantCulture);
                    item.Parameters["CoreSizeMm2"]    = wireCfg.CoreSizeMm2.ToString(CultureInfo.InvariantCulture);
                    item.Parameters["FireResistance"] = wireCfg.FireResistance ?? string.Empty;
                    item.Parameters["Color"]          = wireCfg.Color ?? string.Empty;
                }
                items.Add(item);
            }

            // ── 5. Batteries ──────────────────────────────────────────────────

            // 5a. FACP batteries — one row per panel with battery config
            foreach (var panel in fa.Panels)
            {
                if (!_assignments.PanelAssignments.TryGetValue(panel.DisplayName, out string cfgName)
                    || string.IsNullOrEmpty(cfgName))
                    continue;

                var cfg = _deviceStore.ControlPanels.FirstOrDefault(
                    c => string.Equals(c.Name, cfgName, StringComparison.OrdinalIgnoreCase));
                if (cfg == null || cfg.BatteryUnitAh <= 0) continue;

                var metrics = SystemMetricsCalculator.ComputePanelBatteryMetrics(panel, fa, cfg);
                if (metrics == null) continue;

                var item = new BoqItem
                {
                    Category   = "Batteries \u2014 Control Panel",
                    FamilyName = cfg.Name,
                    TypeName   = $"{metrics.BatteryUnitAh:F1} Ah VRLA",
                    Panel      = panel.DisplayName,
                };
                item.Parameters["Quantity"]             = metrics.BatteriesNeeded.ToString(CultureInfo.InvariantCulture);
                item.Parameters["RequiredCapacity_Ah"]  = metrics.RequiredCapacityAh.ToString("F2", CultureInfo.InvariantCulture);
                item.Parameters["TotalCapacity_Ah"]     = metrics.TotalInstalledAh.ToString("F2", CultureInfo.InvariantCulture);
                item.Parameters["BatteryUnitAh"]        = metrics.BatteryUnitAh.ToString("F1", CultureInfo.InvariantCulture);
                items.Add(item);
            }

            // 5b. Field PSU batteries — one BOQ row per PSU name (SubCircuits grouped)
            if (faDevConfig != null)
            {
                var scsByPsu = new Dictionary<string, List<SubCircuit>>(StringComparer.OrdinalIgnoreCase);
                foreach (var sc in fa.SubCircuits)
                {
                    if (!_assignments.SubCircuitPsuAssignments.TryGetValue(sc.Id, out string psuName)
                        || string.IsNullOrEmpty(psuName))
                        continue;

                    if (!scsByPsu.TryGetValue(psuName, out var list))
                        scsByPsu[psuName] = list = new List<SubCircuit>();
                    list.Add(sc);
                }

                foreach (var psuKvp in scsByPsu)
                {
                    var psuCfg = faDevConfig.PsuUnits.FirstOrDefault(
                        p => string.Equals(p.Name, psuKvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (psuCfg == null || psuCfg.BatteryUnitAh <= 0) continue;

                    var metrics = SystemMetricsCalculator.ComputePsuBatteryMetrics(psuKvp.Value, fa, psuCfg);
                    if (metrics == null) continue;

                    var item = new BoqItem
                    {
                        Category   = "Batteries \u2014 Field PSU",
                        FamilyName = psuKvp.Key,
                        TypeName   = $"{metrics.BatteryUnitAh:F1} Ah VRLA",
                    };
                    item.Parameters["Quantity"]             = metrics.BatteriesNeeded.ToString(CultureInfo.InvariantCulture);
                    item.Parameters["RequiredCapacity_Ah"]  = metrics.RequiredCapacityAh.ToString("F2", CultureInfo.InvariantCulture);
                    item.Parameters["TotalCapacity_Ah"]     = metrics.TotalInstalledAh.ToString("F2", CultureInfo.InvariantCulture);
                    item.Parameters["BatteryUnitAh"]        = metrics.BatteryUnitAh.ToString("F1", CultureInfo.InvariantCulture);
                    items.Add(item);
                }
            }

            return items;
        }

        public IReadOnlyList<string> DiscoverParameterKeys(ModuleData data)
        {
            if (data == null) return Array.Empty<string>();
            var fa = data.GetPayload<FireAlarmPayload>();
            if (fa == null) return Array.Empty<string>();

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Device parameters
            foreach (var device in fa.Devices)
            {
                foreach (var key in device.Properties.Keys)
                    if (!key.StartsWith("_", StringComparison.Ordinal))
                        keys.Add(key);

                keys.Add("Address");
                keys.Add("CurrentDraw_mA");
            }

            // Non-device row parameters
            foreach (var k in new[]
            {
                "Quantity",
                "MaxAddresses", "MaxLoopCount", "MaxMaPerLoop",
                "AddressesPerLoop",
                "CableLength_m", "CoreCount", "CoreSizeMm2", "FireResistance", "Color",
                "RequiredCapacity_Ah", "TotalCapacity_Ah", "BatteryUnitAh",
            })
            keys.Add(k);

            return keys.OrderBy(k => k).ToList();
        }

        public IReadOnlyList<string> GetDefaultVisibleParameterKeys()
        {
            // These structural columns are always meaningful and should appear
            // in the DataGrid immediately — users should not need to discover
            // them manually via the parameter picker.
            return new[]
            {
                "Quantity",
                "MaxAddresses", "MaxLoopCount", "MaxMaPerLoop",
                "AddressesPerLoop",
                "CableLength_m", "CoreCount", "CoreSizeMm2", "FireResistance", "Color",
                "RequiredCapacity_Ah", "TotalCapacity_Ah", "BatteryUnitAh",
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

    }
}
