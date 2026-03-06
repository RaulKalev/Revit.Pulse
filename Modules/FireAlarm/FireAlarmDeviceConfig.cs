using System.Collections.Generic;
using Newtonsoft.Json;
using Pulse.Core.Settings;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Hardware device configuration for the Fire Alarm module.
    /// Contains the control panel library, loop module library, and wire library
    /// used by the Fire Alarm system.
    ///
    /// Stored as a JSON blob in
    /// <see cref="DeviceConfigStore.ModuleConfigBlobs"/>["FireAlarm"] and
    /// accessed via <see cref="DeviceConfigService.LoadModuleConfig{T}"/> /
    /// <see cref="DeviceConfigService.SaveModuleConfig"/>.
    ///
    /// The legacy top-level <c>ControlPanels</c>, <c>LoopModules</c>, and <c>Wires</c>
    /// fields on <see cref="DeviceConfigStore"/> remain for backward compatibility
    /// with existing consumers until those are migrated to read from this type.
    /// </summary>
    public class FireAlarmDeviceConfig : IModuleDeviceConfig
    {
        public string ModuleId => "FireAlarm";

        [JsonProperty("controlPanels")]
        public List<ControlPanelConfig> ControlPanels { get; set; } = new List<ControlPanelConfig>();

        [JsonProperty("loopModules")]
        public List<LoopModuleConfig> LoopModules { get; set; } = new List<LoopModuleConfig>();

        [JsonProperty("wires")]
        public List<WireConfig> Wires { get; set; } = new List<WireConfig>();

        /// <summary>User-defined ancillary NAC/sounder PSU definitions.</summary>
        [JsonProperty("psuUnits")]
        public List<PsuConfig> PsuUnits { get; set; } = new List<PsuConfig>();
    }
}
