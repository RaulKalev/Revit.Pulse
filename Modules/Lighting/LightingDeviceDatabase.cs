using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Container for the hardware device catalog stored at
    /// <c>%APPDATA%\Pulse\lighting-devices.json</c>.
    /// </summary>
    public sealed class LightingDeviceDatabase
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonProperty("devices")]
        public List<LightingDeviceDto> Devices { get; set; } = new List<LightingDeviceDto>();
    }
}
