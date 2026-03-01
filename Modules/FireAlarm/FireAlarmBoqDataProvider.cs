using System;
using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Boq;
using Pulse.Core.Modules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Converts a <see cref="ModuleData"/> snapshot produced by the FireAlarm module
    /// into a flat list of <see cref="BoqItem"/> rows for the BOQ DataGrid.
    ///
    /// One <see cref="BoqItem"/> is created per <see cref="AddressableDevice"/>. 
    /// Standard fields are resolved from the device's well-known properties and from
    /// the topology graph (panel / loop names).  All additional Revit parameters are
    /// forwarded verbatim into <see cref="BoqItem.Parameters"/>.
    /// </summary>
    public class FireAlarmBoqDataProvider : IBoqDataProvider
    {
        public string ModuleKey => "FireAlarm";

        // ── IBoqDataProvider ─────────────────────────────────────────────────

        public IReadOnlyList<BoqItem> GetItems(ModuleData data)
        {
            if (data == null) return Array.Empty<BoqItem>();

            // Build fast lookup maps from the topology.
            var loopById  = data.Loops.ToDictionary(l => l.EntityId, l => l, StringComparer.OrdinalIgnoreCase);
            var panelById = data.Panels.ToDictionary(p => p.EntityId, p => p, StringComparer.OrdinalIgnoreCase);

            var items = new List<BoqItem>(data.Devices.Count);

            foreach (var device in data.Devices)
            {
                var item = new BoqItem
                {
                    ElementId  = device.RevitElementId,
                    Level      = device.LevelName ?? string.Empty,
                };

                // ── Family & type from injected Revit built-in properties ──────────────
                // _FamilyName = Revit Family name  (e.g. "M_Fire Alarm Device")
                // _Name       = Revit Type name    (e.g. "Addressable Smoke Detector 57°C")
                device.Properties.TryGetValue("_FamilyName", out string familyName);
                device.Properties.TryGetValue("_Name",       out string typeName);
                item.FamilyName = familyName ?? string.Empty;
                item.TypeName   = typeName   ?? string.Empty;

                // ── Panel & loop from topology ────────────────────────────────
                if (!string.IsNullOrEmpty(device.LoopId)
                    && loopById.TryGetValue(device.LoopId, out var loop))
                {
                    item.Loop = loop.DisplayName ?? device.LoopId;
                }

                if (!string.IsNullOrEmpty(device.PanelId)
                    && panelById.TryGetValue(device.PanelId, out var panel))
                {
                    item.Panel = panel.DisplayName ?? device.PanelId;
                }

                // ── Category from DeviceType ──────────────────────────────────
                item.Category = device.DeviceType ?? string.Empty;

                // ── All raw Revit parameters ──────────────────────────────────
                foreach (var kvp in device.Properties)
                {
                    // Skip internal keys prefixed with "_"
                    if (kvp.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    item.Parameters[kvp.Key] = kvp.Value;
                }

                // Also expose address and current draw as well-known parameters
                if (!string.IsNullOrEmpty(device.Address))
                    item.Parameters["Address"] = device.Address;
                if (device.CurrentDraw.HasValue)
                    item.Parameters["CurrentDraw_mA"] = device.CurrentDraw.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

                items.Add(item);
            }

            return items;
        }

        public IReadOnlyList<string> DiscoverParameterKeys(ModuleData data)
        {
            if (data == null) return Array.Empty<string>();

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in data.Devices)
            {
                foreach (var key in device.Properties.Keys)
                {
                    if (!key.StartsWith("_", StringComparison.Ordinal))
                        keys.Add(key);
                }

                keys.Add("Address");
                keys.Add("CurrentDraw_mA");
            }

            return keys.OrderBy(k => k).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

    }
}
