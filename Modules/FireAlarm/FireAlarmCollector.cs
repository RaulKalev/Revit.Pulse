using System;
using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Collects fire alarm devices from Revit and populates ModuleData.
    /// Uses the parameter mapping from settings to extract values — no hardcoded parameter names.
    /// </summary>
    public class FireAlarmCollector : IModuleCollector
    {
        public ModuleData Collect(ICollectorContext collectorContext, ModuleSettings settings)
        {
            var data = new ModuleData { ModuleName = "FireAlarm" };

            // Get the list of Revit parameter names we need to extract
            List<string> paramNames = settings.GetAllRevitParameterNames();

            // Collect elements from each configured category
            foreach (string category in settings.Categories)
            {
                var elements = collectorContext.GetElements(category, paramNames);
                ProcessElements(elements, settings, data);
            }

            return data;
        }

        /// <summary>
        /// Process collected elements into typed system entities.
        /// Groups devices by panel and loop.
        /// </summary>
        private void ProcessElements(IReadOnlyList<ElementData> elements, ModuleSettings settings, ModuleData data)
        {
            // Resolve Revit parameter names from logical keys
            string panelParam = settings.GetRevitParameterName(FireAlarmParameterKeys.Panel);
            string loopParam = settings.GetRevitParameterName(FireAlarmParameterKeys.Loop);
            string addressParam = settings.GetRevitParameterName(FireAlarmParameterKeys.Address);
            string deviceTypeParam = settings.GetRevitParameterName(FireAlarmParameterKeys.DeviceType);
            string currentDrawParam = settings.GetRevitParameterName(FireAlarmParameterKeys.CurrentDraw);
            string deviceIdParam = settings.GetRevitParameterName(FireAlarmParameterKeys.DeviceId);

            // Track panels and loops for deduplication
            var panelMap = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
            var loopMap = new Dictionary<string, Loop>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
            {
                // Extract values using the mapped Revit parameter names
                string panelValue = GetParam(element, panelParam);
                string loopValue = GetParam(element, loopParam);
                string addressValue = GetParam(element, addressParam);
                string deviceTypeValue = GetParam(element, deviceTypeParam);
                string currentDrawValue = GetParam(element, currentDrawParam);
                string deviceIdValue = GetParam(element, deviceIdParam);

                // Create or find panel
                string panelKey = string.IsNullOrWhiteSpace(panelValue) ? "(No Panel)" : panelValue.Trim();
                if (!panelMap.TryGetValue(panelKey, out Panel panel))
                {
                    panel = new Panel($"panel_{panelKey}", panelKey);
                    panelMap[panelKey] = panel;
                    data.Panels.Add(panel);
                }

                // Create or find loop (scoped to panel)
                string loopKey = string.IsNullOrWhiteSpace(loopValue) ? "(No Loop)" : loopValue.Trim();
                string compositeLoopKey = $"{panelKey}::{loopKey}";
                if (!loopMap.TryGetValue(compositeLoopKey, out Loop loop))
                {
                    loop = new Loop($"loop_{compositeLoopKey}", loopKey)
                    {
                        PanelId = panel.EntityId
                    };
                    loopMap[compositeLoopKey] = loop;
                    panel.Loops.Add(loop);
                    data.Loops.Add(loop);
                }

                // Create device
                string deviceLabel = !string.IsNullOrWhiteSpace(addressValue)
                    ? $"{deviceTypeValue ?? "Device"} [{addressValue}]"
                    : $"Device (Element {element.ElementId})";

                var device = new AddressableDevice($"device_{element.ElementId}", deviceLabel)
                {
                    RevitElementId = element.ElementId,
                    Address = addressValue,
                    DeviceType = deviceTypeValue,
                    LoopId = loop.EntityId,
                    PanelId = panel.EntityId,
                };

                // Parse current draw
                if (!string.IsNullOrWhiteSpace(currentDrawValue) && double.TryParse(
                    currentDrawValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double currentDraw))
                {
                    device.CurrentDraw = currentDraw;
                }

                // Store all extracted properties on the device
                foreach (var kvp in element.Parameters)
                {
                    device.SetProperty(kvp.Key, kvp.Value);
                }

                loop.Devices.Add(device);
                data.Devices.Add(device);
            }
        }

        /// <summary>
        /// Safely get a parameter value from element data.
        /// </summary>
        private static string GetParam(ElementData element, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName))
            {
                return null;
            }

            element.Parameters.TryGetValue(paramName, out string value);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
