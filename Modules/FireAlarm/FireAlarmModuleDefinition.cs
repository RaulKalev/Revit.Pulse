using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Module definition for the Fire Alarm system.
    /// Registers the module with the Pulse platform and provides factory methods
    /// for creating collector, topology builder, and rule pack instances.
    /// </summary>
    public class FireAlarmModuleDefinition : IModuleDefinition
    {
        public string ModuleId => "FireAlarm";
        public string DisplayName => "Fire Alarm";
        public string Description => "Addressable fire alarm system management — panels, loops, zones, and devices.";
        public string Version => "1.0.0";

        /// <summary>
        /// Returns the default settings for the Fire Alarm module.
        /// These are used when no settings are stored in the document.
        /// Parameter names here match common Estonian fire alarm conventions.
        /// Users can customize them in the settings UI.
        /// </summary>
        public ModuleSettings GetDefaultSettings()
        {
            return new ModuleSettings
            {
                ModuleId = ModuleId,
                SchemaVersion = 1,
                Categories = new System.Collections.Generic.List<string>
                {
                    "Fire Alarm Devices"
                },
                ParameterMappings = new System.Collections.Generic.List<ParameterMapping>
                {
                    new ParameterMapping(FireAlarmParameterKeys.Panel, "Panel", isRequired: true),
                    new ParameterMapping(FireAlarmParameterKeys.Loop, "Loop", isRequired: true),
                    new ParameterMapping(FireAlarmParameterKeys.Address, "Aadress", isRequired: true),
                    new ParameterMapping(FireAlarmParameterKeys.DeviceType, "Device type", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.CurrentDraw, "Current draw", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.DeviceId, "id", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.PanelConfig, "FA_Panel_Config", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.LoopModuleConfig, "FA_Loop_Config", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.PanelElementCategory, "Electrical Equipment", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.PanelElementNameParam, "Mark", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.CircuitElementId, "FA_Circuit_ElementId", isRequired: false),
                    new ParameterMapping(FireAlarmParameterKeys.Wire, "FA_Wire", isRequired: false),
                }
            };
        }

        public IModuleCollector CreateCollector() => new FireAlarmCollector();
        public ITopologyBuilder CreateTopologyBuilder() => new FireAlarmTopologyBuilder();
        public IRulePack CreateRulePack() => new FireAlarmRulePack();
    }
}
