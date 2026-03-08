using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pulse.Core.Boq;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Converts a <see cref="ModuleData"/> snapshot produced by the Lighting module
    /// into a flat list of <see cref="BoqItem"/> rows for the BOQ DataGrid.
    ///
    /// Emits two row groups in order:
    ///   1. Lighting devices (one row each)
    ///   2. Controllers      (one row per discovered controller)
    /// </summary>
    public class LightingBoqDataProvider : IBoqDataProvider
    {
        private readonly TopologyAssignmentsStore _assignments;
        private readonly DeviceConfigStore        _deviceStore;

        public LightingBoqDataProvider(
            TopologyAssignmentsStore assignments,
            DeviceConfigStore        deviceStore)
        {
            _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            _deviceStore = deviceStore  ?? throw new ArgumentNullException(nameof(deviceStore));
        }

        public string ModuleKey => "Lighting";

        public IReadOnlyList<BoqItem> GetItems(ModuleData data)
        {
            if (data == null) return Array.Empty<BoqItem>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return Array.Empty<BoqItem>();

            var lineById  = lg.Lines.ToDictionary(l => l.EntityId, l => l, StringComparer.OrdinalIgnoreCase);
            var ctrlById  = lg.Controllers.ToDictionary(c => c.EntityId, c => c, StringComparer.OrdinalIgnoreCase);

            var items = new List<BoqItem>();

            // ── 1. Lighting devices ───────────────────────────────────────────
            foreach (var device in lg.Devices)
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
                    && lineById.TryGetValue(device.LoopId, out var devLine))
                    item.Loop = devLine.DisplayName ?? device.LoopId;

                if (!string.IsNullOrEmpty(device.PanelId)
                    && ctrlById.TryGetValue(device.PanelId, out var devCtrl))
                    item.Panel = devCtrl.DisplayName ?? device.PanelId;

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

            // ── 2. Controllers ────────────────────────────────────────────────
            foreach (var ctrl in lg.Controllers)
            {
                var item = new BoqItem
                {
                    ElementId  = ctrl.RevitElementId,
                    Category   = "Controllers",
                    FamilyName = ctrl.DisplayName,
                    TypeName   = "Lighting Controller",
                    Panel      = ctrl.DisplayName,
                };
                item.Parameters["Line Count"] = ctrl.Loops.Count.ToString(CultureInfo.InvariantCulture);
                item.Parameters["Total Devices"] = ctrl.Loops.Sum(l => l.Devices.Count).ToString(CultureInfo.InvariantCulture);
                items.Add(item);
            }

            return items;
        }

        public IReadOnlyList<string> DiscoverParameterKeys(ModuleData data)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in GetItems(data))
                foreach (var k in item.Parameters.Keys)
                    keys.Add(k.ToString());
            return new List<string>(keys);
        }

        public IReadOnlyList<string> GetDefaultVisibleParameterKeys()
        {
            return new List<string> { "Address", "CurrentDraw_mA" };
        }
    }
}
