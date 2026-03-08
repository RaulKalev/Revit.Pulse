using System;
using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Collects lighting devices from Revit and populates ModuleData.
    /// Uses the parameter mapping from settings to extract values — no hardcoded Revit parameter names.
    ///
    /// Terminology mapping:
    ///   Controller → Panel (top-level container in core model)
    ///   Line       → Loop  (channel within a controller)
    ///   Luminaire  → AddressableDevice (leaf device)
    /// </summary>
    public class LightingCollector : IModuleCollector
    {
        public ModuleData Collect(ICollectorContext collectorContext, ModuleSettings settings)
        {
            var data = new ModuleData { ModuleName = "Lighting" };
            var payload = new LightingPayload();
            data.Payload = payload;

            // Get the list of Revit parameter names we need to extract
            List<string> paramNames = settings.GetAllRevitParameterNames();

            // Collect elements from each configured category
            foreach (string category in settings.Categories)
            {
                var elements = collectorContext.GetElements(category, paramNames);
                ProcessElements(elements, settings, payload);
            }

            // Second pass: resolve Controller.RevitElementId from a dedicated element category
            ResolveControllerElementIds(collectorContext, settings, payload);

            return data;
        }

        /// <summary>
        /// Looks up controller elements in the user-configured category and matches them
        /// to topology controllers by name, populating <see cref="Panel.RevitElementId"/>.
        /// </summary>
        private static void ResolveControllerElementIds(
            ICollectorContext collectorContext,
            ModuleSettings settings,
            LightingPayload payload)
        {
            string ctrlCategory  = settings.GetRevitParameterName(LightingParameterKeys.ControllerElementCategory);
            string ctrlNameParam = settings.GetRevitParameterName(LightingParameterKeys.ControllerElementNameParam);

            if (string.IsNullOrWhiteSpace(ctrlCategory))
                return;

            // Quick lookup: controller display name → Panel
            var ctrlByLabel = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
            foreach (var ctrl in payload.Controllers)
                ctrlByLabel[ctrl.DisplayName] = ctrl;

            var paramList = new List<string>();
            if (!string.IsNullOrWhiteSpace(ctrlNameParam))
                paramList.Add(ctrlNameParam);

            var ctrlElements = collectorContext.GetElements(ctrlCategory, paramList);

            foreach (var element in ctrlElements)
            {
                Panel matched = null;

                // 1. Try configured parameter — exact match
                if (!string.IsNullOrWhiteSpace(ctrlNameParam)
                    && element.Parameters.TryGetValue(ctrlNameParam, out string paramValue)
                    && !string.IsNullOrWhiteSpace(paramValue))
                {
                    ctrlByLabel.TryGetValue(paramValue.Trim(), out matched);
                }

                // 2. Try built-in element Name — exact match
                if (matched == null
                    && element.Parameters.TryGetValue("_Name", out string elemName)
                    && !string.IsNullOrWhiteSpace(elemName))
                {
                    ctrlByLabel.TryGetValue(elemName.Trim(), out matched);
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
                }
            }
        }

        /// <summary>
        /// Process collected elements into typed system entities.
        /// Groups devices by controller and line.
        /// </summary>
        private void ProcessElements(IReadOnlyList<ElementData> elements, ModuleSettings settings, LightingPayload payload)
        {
            // Resolve Revit parameter names from logical keys
            string controllerParam = settings.GetRevitParameterName(LightingParameterKeys.Controller);
            string lineParam       = settings.GetRevitParameterName(LightingParameterKeys.Line);
            string addressParam    = settings.GetRevitParameterName(LightingParameterKeys.Address);
            string deviceTypeParam = settings.GetRevitParameterName(LightingParameterKeys.DeviceType);
            string currentDrawParam = settings.GetRevitParameterName(LightingParameterKeys.CurrentDraw);
            string deviceIdParam   = settings.GetRevitParameterName(LightingParameterKeys.DeviceId);
            string systemTypeParam = settings.GetRevitParameterName(LightingParameterKeys.SystemType);
            // Hidden-from-display params
            string ctrlElementCategoryParam = settings.GetRevitParameterName(LightingParameterKeys.ControllerElementCategory);
            string ctrlElementNameParam     = settings.GetRevitParameterName(LightingParameterKeys.ControllerElementNameParam);

            // Track controllers and lines for deduplication
            var controllerMap = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
            var lineMap       = new Dictionary<string, Loop>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
            {
                // Extract values using the mapped Revit parameter names
                string controllerValue = GetParam(element, controllerParam);
                string lineValue       = GetParam(element, lineParam);
                string addressValue    = GetParam(element, addressParam);
                string deviceTypeValue = GetParam(element, deviceTypeParam);
                string deviceIdValue   = GetParam(element, deviceIdParam);

                // Create or find controller
                string ctrlKey = string.IsNullOrWhiteSpace(controllerValue) ? "(No Controller)" : controllerValue.Trim();
                if (!controllerMap.TryGetValue(ctrlKey, out Panel controller))
                {
                    controller = new Panel($"ctrl_{ctrlKey}", ctrlKey);
                    controllerMap[ctrlKey] = controller;
                    payload.Controllers.Add(controller);
                }

                // Create or find line (scoped to controller)
                string lineKey = string.IsNullOrWhiteSpace(lineValue) ? "(No Line)" : lineValue.Trim();
                string compositeLineKey = $"{ctrlKey}::{lineKey}";
                if (!lineMap.TryGetValue(compositeLineKey, out Loop line))
                {
                    line = new Loop($"line_{compositeLineKey}", lineKey)
                    {
                        PanelId = controller.EntityId
                    };
                    lineMap[compositeLineKey] = line;
                    controller.Loops.Add(line);
                    payload.Lines.Add(line);
                }

                // Create device
                string deviceLabel = !string.IsNullOrWhiteSpace(addressValue)
                    ? $"{deviceTypeValue ?? "Luminaire"} [{addressValue}]"
                    : $"Luminaire (Element {element.ElementId})";

                var device = new AddressableDevice($"device_{element.ElementId}", deviceLabel)
                {
                    RevitElementId = element.ElementId,
                    Address = addressValue,
                    DeviceType = deviceTypeValue,
                    LoopId = line.EntityId,
                    PanelId = controller.EntityId,
                };

                // Parse current draw for capacity calculations
                string currentDrawRaw = GetParam(element, currentDrawParam);
                if (!string.IsNullOrWhiteSpace(currentDrawRaw) && double.TryParse(
                    currentDrawRaw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double currentDrawParsed))
                {
                    device.CurrentDraw = currentDrawParsed;
                }

                // Store all extracted properties on the device.
                // Skip params that are semantically captured above or hidden from display.
                foreach (var kvp in element.Parameters)
                {
                    if (IsSkippedParam(kvp.Key, controllerParam, lineParam, addressParam,
                        deviceTypeParam, currentDrawParam, deviceIdParam, systemTypeParam,
                        ctrlElementCategoryParam, ctrlElementNameParam))
                        continue;
                    device.SetProperty(kvp.Key, kvp.Value);
                }

                // Store relabeled/semantic values under internal keys for the topology builder.
                if (!string.IsNullOrEmpty(lineValue))
                    device.SetProperty("_LineValue", lineValue);
                if (!string.IsNullOrEmpty(currentDrawRaw))
                    device.SetProperty("_CurrentDraw", currentDrawRaw);
                string systemTypeValue = GetParam(element, systemTypeParam);
                if (!string.IsNullOrEmpty(systemTypeValue))
                    device.SetProperty("_SystemType", systemTypeValue);

                // Capture the element's level name
                if (element.Parameters.TryGetValue("_LevelName", out string levelNameVal)
                    && !string.IsNullOrWhiteSpace(levelNameVal))
                    device.LevelName = levelNameVal;

                // Capture the element's elevation
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

                line.Devices.Add(device);
                payload.Devices.Add(device);
            }
        }

        private static bool IsSkippedParam(string key, params string[] skipped)
        {
            foreach (string s in skipped)
            {
                if (!string.IsNullOrEmpty(s)
                    && string.Equals(key, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string GetParam(ElementData element, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName))
                return null;

            element.Parameters.TryGetValue(paramName, out string value);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
