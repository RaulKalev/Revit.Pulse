using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Pulse.Core.Settings;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Hardware device configuration for the Lighting module.
    /// Contains controller library and capacity defaults.
    ///
    /// Stored as a JSON blob in
    /// <see cref="DeviceConfigStore.ModuleConfigBlobs"/>["Lighting"] and
    /// accessed via <see cref="DeviceConfigService.LoadModuleConfig{T}"/> /
    /// <see cref="DeviceConfigService.SaveModuleConfig"/>.
    ///
    /// The first supported protocol is DALI — defaults reflect DALI-2 specifications.
    /// Future lighting systems can extend or override these defaults via separate
    /// system profiles.
    /// </summary>
    public class LightingDeviceConfig : IModuleDeviceConfig
    {
        public string ModuleId => "Lighting";

        /// <summary>Known lighting controller hardware profiles.</summary>
        [JsonProperty("controllers")]
        public List<LightingControllerConfig> Controllers { get; set; } = new List<LightingControllerConfig>();
    }

    /// <summary>
    /// Configuration record for a lighting controller (e.g. DALI gateway, Sympolight controller).
    /// </summary>
    public class LightingControllerConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "New Controller";

        /// <summary>Lighting system / protocol this controller uses (e.g. "DALI", "Sympolight").</summary>
        [JsonProperty("systemType")]
        public string SystemType { get; set; } = "DALI";

        /// <summary>Maximum number of device addresses per line (DALI default: 64).</summary>
        [JsonProperty("maxAddressesPerLine")]
        public int MaxAddressesPerLine { get; set; } = 64;

        /// <summary>Maximum number of lines this controller supports.</summary>
        [JsonProperty("maxLines")]
        public int MaxLines { get; set; } = 4;

        /// <summary>Maximum current draw per line in mA (DALI default: 250 mA).</summary>
        [JsonProperty("maxMaPerLine")]
        public double MaxMaPerLine { get; set; } = 250.0;
    }
}
