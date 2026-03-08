using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Module definition for the Lighting system.
    /// Registers the module with the Pulse platform and provides factory methods
    /// for creating collector, topology builder, and rule pack instances.
    ///
    /// The first supported lighting protocol is DALI. The module is named broadly
    /// ("Lighting") so future systems (Sympolight, KNX, etc.) can be added as
    /// additional profiles without renaming or forking the module.
    ///
    /// Capabilities are intentionally narrower than Fire Alarm for the initial
    /// release — diagram, wiring, and symbol mapping are not yet implemented.
    /// </summary>
    public class LightingModuleDefinition : IModuleDefinition,
        IProvidesDeviceConfig
    {
        public string ModuleId => "Lighting";
        public string DisplayName => "Lighting";
        public string Description => "Addressable lighting system management — controllers, lines, and luminaires.";
        public string Version => "1.0.0";

        /// <summary>
        /// Lighting supports capacity gauges and config assignment.
        /// Diagram, wiring, and symbol mapping may be added in future versions.
        /// </summary>
        public ModuleCapabilities Capabilities =>
            ModuleCapabilities.CapacityGauges | ModuleCapabilities.ConfigAssignment;

        /// <summary>
        /// Returns the default settings for the Lighting module.
        /// Parameter names default to common DALI parameter conventions in Revit.
        /// Users can customise them in the settings UI to match their model.
        /// </summary>
        public ModuleSettings GetDefaultSettings()
        {
            return new ModuleSettings
            {
                ModuleId = ModuleId,
                SchemaVersion = 1,
                Categories = new System.Collections.Generic.List<string>
                {
                    "Lighting Devices"
                },
                ParameterMappings = new System.Collections.Generic.List<ParameterMapping>
                {
                    new ParameterMapping(LightingParameterKeys.Controller, "DALI_Controller", isRequired: true),
                    new ParameterMapping(LightingParameterKeys.Line, "DALI_Line", isRequired: true),
                    new ParameterMapping(LightingParameterKeys.Address, "DALI_Address", isRequired: true),
                    new ParameterMapping(LightingParameterKeys.DeviceType, "Device type", isRequired: false),
                    new ParameterMapping(LightingParameterKeys.CurrentDraw, "Current draw (mA)", isRequired: false),
                    new ParameterMapping(LightingParameterKeys.DeviceId, "id", isRequired: false),
                    new ParameterMapping(LightingParameterKeys.ControllerElementCategory, "Electrical Equipment", isRequired: false),
                    new ParameterMapping(LightingParameterKeys.ControllerElementNameParam, "Mark", isRequired: false),
                    new ParameterMapping(LightingParameterKeys.SystemType, "DALI_SystemType", isRequired: false),
                }
            };
        }

        public IModuleCollector CreateCollector() => new LightingCollector();
        public ITopologyBuilder CreateTopologyBuilder() => new LightingTopologyBuilder();
        public IRulePack CreateRulePack() => new LightingRulePack();

        // ── IProvidesDeviceConfig ────────────────────────────────────────────
        public IModuleDeviceConfig GetDefaultDeviceConfig() => new LightingDeviceConfig();
    }
}
