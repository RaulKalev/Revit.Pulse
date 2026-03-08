using Newtonsoft.Json;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Hardware reference record for a lighting controller or accessory.
    /// Stored in the device catalog (<c>%APPDATA%\Pulse\lighting-devices.json</c>).
    /// </summary>
    public sealed class LightingDeviceDto
    {
        [JsonProperty("id")]
        public string Id { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>"controller", "power_supply", or "repeater".</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "controller";

        [JsonProperty("manufacturer")]
        public string Manufacturer { get; set; } = "";

        [JsonProperty("model")]
        public string Model { get; set; } = "";

        /// <summary>Number of independent DALI lines supported.</summary>
        [JsonProperty("daliLines")]
        public int DaliLines { get; set; } = 1;

        /// <summary>Maximum addressable devices per line (DALI default: 64).</summary>
        [JsonProperty("maxAddressesPerLine")]
        public int MaxAddressesPerLine { get; set; } = 64;

        /// <summary>Rated bus current per line in mA.</summary>
        [JsonProperty("ratedCurrentMaPerLine")]
        public double RatedCurrentMaPerLine { get; set; } = 250.0;

        /// <summary>Guaranteed minimum bus current per line in mA.</summary>
        [JsonProperty("guaranteedCurrentMaPerLine")]
        public double GuaranteedCurrentMaPerLine { get; set; } = 200.0;
    }
}
