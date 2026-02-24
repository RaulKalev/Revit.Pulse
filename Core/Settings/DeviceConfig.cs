using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Configuration record for a Fire Alarm Control Panel (FACP).
    /// </summary>
    public class ControlPanelConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "New Panel";

        /// <summary>
        /// The address(es) this panel occupies in the system (e.g. "1" or "1,2,3" or "1-4").
        /// </summary>
        [JsonProperty("panelAddresses")]
        public string PanelAddresses { get; set; } = "1";

        /// <summary>
        /// Maximum number of SLC device addresses available per loop card.
        /// </summary>
        [JsonProperty("addressesPerLoop")]
        public int AddressesPerLoop { get; set; } = 127;

        /// <summary>
        /// Maximum number of loop cards that can be installed in this panel.
        /// </summary>
        [JsonProperty("maxLoopCount")]
        public int MaxLoopCount { get; set; } = 8;

        /// <summary>
        /// Maximum milliamps the loop card can supply (SLC current capacity).
        /// </summary>
        [JsonProperty("maxMaPerLoop")]
        public double MaxMaPerLoop { get; set; } = 500.0;
    }

    /// <summary>
    /// Configuration record for a Loop Expansion Module / Loop Controller.
    /// </summary>
    public class LoopModuleConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "New Loop Module";

        /// <summary>
        /// The address(es) this module occupies on the parent panel.
        /// </summary>
        [JsonProperty("panelAddresses")]
        public string PanelAddresses { get; set; } = "1";

        /// <summary>
        /// Maximum number of SLC device addresses available on this module.
        /// </summary>
        [JsonProperty("addressesPerLoop")]
        public int AddressesPerLoop { get; set; } = 127;

        /// <summary>
        /// Maximum number of loop outputs provided by this module.
        /// </summary>
        [JsonProperty("maxLoopCount")]
        public int MaxLoopCount { get; set; } = 2;

        /// <summary>
        /// Maximum milliamps the module can supply per loop.
        /// </summary>
        [JsonProperty("maxMaPerLoop")]
        public double MaxMaPerLoop { get; set; } = 500.0;
    }

    /// <summary>
    /// Root object persisted to the local JSON file.
    /// Contains all configured control panels and loop modules.
    /// </summary>
    public class DeviceConfigStore
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("controlPanels")]
        public List<ControlPanelConfig> ControlPanels { get; set; } = new List<ControlPanelConfig>();

        [JsonProperty("loopModules")]
        public List<LoopModuleConfig> LoopModules { get; set; } = new List<LoopModuleConfig>();

        /// <summary>Panel label → assigned ControlPanelConfig name.</summary>
        [JsonProperty("panelAssignments")]
        public Dictionary<string, string> PanelAssignments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loop label → assigned LoopModuleConfig name.</summary>
        [JsonProperty("loopAssignments")]
        public Dictionary<string, string> LoopAssignments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → true means draw wire on the right side of the panel.</summary>
        [JsonProperty("loopFlipStates")]
        public Dictionary<string, bool> LoopFlipStates { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → number of extra horizontal lines added (total wires = 2 + value).</summary>
        [JsonProperty("loopExtraLines")]
        public Dictionary<string, int> LoopExtraLines { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Level name → overridden elevation (Revit feet), set by Move mode in the diagram.</summary>
        [JsonProperty("levelElevationOffsets")]
        public Dictionary<string, double> LevelElevationOffsets { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }
}
