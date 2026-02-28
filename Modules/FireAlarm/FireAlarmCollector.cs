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

            // Second pass: resolve Panel.RevitElementId from a dedicated element category
            ResolvePanelElementIds(collectorContext, settings, data);

            return data;
        }

        /// <summary>
        /// Looks up panel board elements in the user-configured category and matches them
        /// to topology panels by name, populating <see cref="Panel.RevitElementId"/>.
        /// </summary>
        private static void ResolvePanelElementIds(
            ICollectorContext collectorContext,
            ModuleSettings settings,
            ModuleData data)
        {
            string panelCategory  = settings.GetRevitParameterName(FireAlarmParameterKeys.PanelElementCategory);
            string panelNameParam = settings.GetRevitParameterName(FireAlarmParameterKeys.PanelElementNameParam);

            if (string.IsNullOrWhiteSpace(panelCategory))
                return;

            // Quick lookup: panel display name → Panel
            var panelByLabel = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
            foreach (var panel in data.Panels)
                panelByLabel[panel.DisplayName] = panel;

            var paramList = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(panelNameParam))
                paramList.Add(panelNameParam);
            // _Name is always injected by the collector service — no need to request it.

            var panelElements = collectorContext.GetElements(panelCategory, paramList);

            foreach (var element in panelElements)
            {
                Panel matched = null;

                // 1. Try configured parameter — exact match
                if (!string.IsNullOrWhiteSpace(panelNameParam)
                    && element.Parameters.TryGetValue(panelNameParam, out string paramValue)
                    && !string.IsNullOrWhiteSpace(paramValue))
                {
                    panelByLabel.TryGetValue(paramValue.Trim(), out matched);
                }

                // 2. Try built-in element Name — exact match
                if (matched == null
                    && element.Parameters.TryGetValue("_Name", out string elemName)
                    && !string.IsNullOrWhiteSpace(elemName))
                {
                    panelByLabel.TryGetValue(elemName.Trim(), out matched);
                }

                // 3. Try built-in element Name — starts-with match
                //    Handles cases like type name "ATS keskseade, Pea" matching panel "ATS keskseade"
                if (matched == null
                    && element.Parameters.TryGetValue("_Name", out string elemName2)
                    && !string.IsNullOrWhiteSpace(elemName2))
                {
                    foreach (var kvp in panelByLabel)
                    {
                        if (elemName2.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = kvp.Value;
                            break;
                        }
                    }
                }

                if (matched != null)
                {
                    if (!matched.RevitElementId.HasValue)
                        matched.RevitElementId = element.ElementId;

                    if (!matched.Elevation.HasValue
                        && element.Parameters.TryGetValue("_LevelElevation", out string elevStr)
                        && double.TryParse(elevStr,
                               System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out double elev))
                    {
                        matched.Elevation = elev;
                    }

                    // Capture the panel element's XYZ location for cable-length routing.
                    if (!matched.LocationX.HasValue
                        && element.Parameters.TryGetValue("_LocationX", out string plxStr)
                        && double.TryParse(plxStr, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out double plx))
                        matched.LocationX = plx;
                    if (!matched.LocationY.HasValue
                        && element.Parameters.TryGetValue("_LocationY", out string plyStr)
                        && double.TryParse(plyStr, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out double ply))
                        matched.LocationY = ply;
                    if (!matched.LocationZ.HasValue
                        && element.Parameters.TryGetValue("_LocationZ", out string plzStr)
                        && double.TryParse(plzStr, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out double plz))
                        matched.LocationZ = plz;
                }
            }
        }

        /// <summary>
        /// Process collected elements into typed system entities.
        /// Groups devices by panel and loop.
        /// </summary>
        private void ProcessElements(IReadOnlyList<ElementData> elements, ModuleSettings settings, ModuleData data)
        {
            // Resolve Revit parameter names from logical keys
            string panelParam           = settings.GetRevitParameterName(FireAlarmParameterKeys.Panel);
            string loopParam            = settings.GetRevitParameterName(FireAlarmParameterKeys.Loop);
            string addressParam         = settings.GetRevitParameterName(FireAlarmParameterKeys.Address);
            string deviceTypeParam      = settings.GetRevitParameterName(FireAlarmParameterKeys.DeviceType);
            string currentDrawNormalParam = settings.GetRevitParameterName(FireAlarmParameterKeys.CurrentDrawNormal);
            string currentDrawAlarmParam  = settings.GetRevitParameterName(FireAlarmParameterKeys.CurrentDrawAlarm);
            string deviceIdParam        = settings.GetRevitParameterName(FireAlarmParameterKeys.DeviceId);
            string circuitElementIdParam = settings.GetRevitParameterName(FireAlarmParameterKeys.CircuitElementId);
            string panelConfigParam      = settings.GetRevitParameterName(FireAlarmParameterKeys.PanelConfig);
            string loopModuleConfigParam = settings.GetRevitParameterName(FireAlarmParameterKeys.LoopModuleConfig);
            string wireParam             = settings.GetRevitParameterName(FireAlarmParameterKeys.Wire);
            // Hidden-from-display params (values captured internally but not shown in properties panel)
            string panelElementCategoryParam = settings.GetRevitParameterName(FireAlarmParameterKeys.PanelElementCategory);
            string panelElementNameParam     = settings.GetRevitParameterName(FireAlarmParameterKeys.PanelElementNameParam);

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

                // Resolve Loop.RevitElementId from the circuit element ID parameter (first device wins)
                if (!loop.RevitElementId.HasValue)
                {
                    string circuitIdValue = GetParam(element, circuitElementIdParam);
                    if (!string.IsNullOrWhiteSpace(circuitIdValue)
                        && long.TryParse(circuitIdValue, out long circuitId))
                    {
                        loop.RevitElementId = circuitId;
                    }
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

                // Parse normal current draw for device.CurrentDraw (used by gauge calculations).
                string currentDrawNormalRaw = GetParam(element, currentDrawNormalParam);
                if (!string.IsNullOrWhiteSpace(currentDrawNormalRaw) && double.TryParse(
                    currentDrawNormalRaw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double currentDrawNormalParsed))
                {
                    device.CurrentDraw = currentDrawNormalParsed;
                }

                // Store all extracted properties on the device.
                // Skip raw Revit param names for semantically-captured and relabeled values —
                // they are re-emitted with friendly display labels by the topology builder.
                // Also skip params hidden from the properties panel entirely.
                foreach (var kvp in element.Parameters)
                {
                    if (!string.IsNullOrEmpty(panelParam) &&
                        string.Equals(kvp.Key, panelParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(loopParam) &&
                        string.Equals(kvp.Key, loopParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(addressParam) &&
                        string.Equals(kvp.Key, addressParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(deviceTypeParam) &&
                        string.Equals(kvp.Key, deviceTypeParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(currentDrawNormalParam) &&
                        string.Equals(kvp.Key, currentDrawNormalParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(currentDrawAlarmParam) &&
                        string.Equals(kvp.Key, currentDrawAlarmParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(panelConfigParam) &&
                        string.Equals(kvp.Key, panelConfigParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(loopModuleConfigParam) &&
                        string.Equals(kvp.Key, loopModuleConfigParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(wireParam) &&
                        string.Equals(kvp.Key, wireParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(deviceIdParam) &&
                        string.Equals(kvp.Key, deviceIdParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(circuitElementIdParam) &&
                        string.Equals(kvp.Key, circuitElementIdParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(panelElementCategoryParam) &&
                        string.Equals(kvp.Key, panelElementCategoryParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrEmpty(panelElementNameParam) &&
                        string.Equals(kvp.Key, panelElementNameParam, StringComparison.OrdinalIgnoreCase))
                        continue;
                    device.SetProperty(kvp.Key, kvp.Value);
                }

                // Store relabeled/semantic values under internal keys for the topology builder.
                if (!string.IsNullOrEmpty(loopValue))
                    device.SetProperty("_LoopValue", loopValue);
                string currentDrawNormalValue = GetParam(element, currentDrawNormalParam);
                if (!string.IsNullOrEmpty(currentDrawNormalValue))
                    device.SetProperty("_CurrentDrawNormal", currentDrawNormalValue);
                string currentDrawAlarmValue = GetParam(element, currentDrawAlarmParam);
                if (!string.IsNullOrEmpty(currentDrawAlarmValue))
                    device.SetProperty("_CurrentDrawAlarm", currentDrawAlarmValue);
                string panelConfigValue = GetParam(element, panelConfigParam);
                if (!string.IsNullOrEmpty(panelConfigValue))
                    device.SetProperty("_PanelConfig", panelConfigValue);
                string loopModuleConfigValue = GetParam(element, loopModuleConfigParam);
                if (!string.IsNullOrEmpty(loopModuleConfigValue))
                    device.SetProperty("_LoopModuleConfig", loopModuleConfigValue);
                string wireValue = GetParam(element, wireParam);
                if (!string.IsNullOrEmpty(wireValue))
                    device.SetProperty("_Wire", wireValue);

                // Capture the element's level name so the diagram can group devices into the correct level band.
                if (element.Parameters.TryGetValue("_LevelName", out string levelNameVal)
                    && !string.IsNullOrWhiteSpace(levelNameVal))
                    device.LevelName = levelNameVal;

                // Capture the element's "Elevation from Level" offset (feet) for display in the inspector.
                // Fall back to the host level's base elevation if the offset param is unavailable.
                if (element.Parameters.TryGetValue("_ElevationFromLevel", out string elevFromLevelStr)
                    && double.TryParse(elevFromLevelStr,
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out double elevFromLevel))
                {
                    device.Elevation = elevFromLevel;
                }
                else if (element.Parameters.TryGetValue("_LevelElevation", out string devElevStr)
                    && double.TryParse(devElevStr,
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out double devElev))
                {
                    device.Elevation = devElev;
                }

                // Capture the element's XYZ location for cable-length routing calculations.
                if (element.Parameters.TryGetValue("_LocationX", out string lxStr)
                    && double.TryParse(lxStr, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out double lx))
                    device.LocationX = lx;
                if (element.Parameters.TryGetValue("_LocationY", out string lyStr)
                    && double.TryParse(lyStr, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out double ly))
                    device.LocationY = ly;
                if (element.Parameters.TryGetValue("_LocationZ", out string lzStr)
                    && double.TryParse(lzStr, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out double lz))
                    device.LocationZ = lz;

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
