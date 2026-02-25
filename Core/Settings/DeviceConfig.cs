using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Pulse.Core.Settings;

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
    /// Configuration record for a wire type used in a fire-alarm installation.
    /// </summary>
    public class WireConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "New Wire";

        /// <summary>Number of conductors in the cable (e.g. 2 for a standard loop cable).</summary>
        [JsonProperty("coreCount")]
        public int CoreCount { get; set; } = 2;

        /// <summary>Cross‑section area of each conductor in mm².</summary>
        [JsonProperty("coreSizeMm2")]
        public double CoreSizeMm2 { get; set; } = 0.5;

        /// <summary>Cable colour designation (e.g. "Red/Black", "Grey").</summary>
        [JsonProperty("color")]
        public string Color { get; set; } = string.Empty;

        /// <summary>Whether the cable has an overall or individual-core shield/screen.</summary>
        [JsonProperty("hasShielding")]
        public bool HasShielding { get; set; }

        /// <summary>Fire-resistance designation (e.g. "FP200", "E30") or empty when not rated.</summary>
        [JsonProperty("fireResistance")]
        public string FireResistance { get; set; } = string.Empty;
    }

    /// <summary>
    /// A named paper size (width and height in millimetres) used for diagram exports/printing.
    /// </summary>
    public class PaperSizeConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "A4";

        /// <summary>Paper width in millimetres (long edge for landscape).</summary>
        [JsonProperty("widthMm")]
        public double WidthMm { get; set; } = 297.0;

        /// <summary>Paper height in millimetres (short edge for landscape).</summary>
        [JsonProperty("heightMm")]
        public double HeightMm { get; set; } = 210.0;

        /// <summary>Margins from paper edge to drawing area, in millimetres.</summary>
        [JsonProperty("marginLeftMm")]
        public double MarginLeftMm   { get; set; } = 10.0;

        [JsonProperty("marginTopMm")]
        public double MarginTopMm    { get; set; } = 10.0;

        [JsonProperty("marginRightMm")]
        public double MarginRightMm  { get; set; } = 10.0;

        [JsonProperty("marginBottomMm")]
        public double MarginBottomMm { get; set; } = 10.0;
    }

    /// <summary>
    /// Root object persisted to the local JSON file.
    /// Contains the device library: control-panel types, loop-module types, and wire types.
    ///
    /// Per-document assignments (which config is assigned in a given model, flip states, etc.)
    /// are stored in Revit Extensible Storage via <c>TopologyAssignmentsStore</c>.
    /// </summary>
    public class DeviceConfigStore
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("controlPanels")]
        public List<ControlPanelConfig> ControlPanels { get; set; } = new List<ControlPanelConfig>();

        [JsonProperty("loopModules")]
        public List<LoopModuleConfig> LoopModules { get; set; } = new List<LoopModuleConfig>();

        /// <summary>User-defined cable/wire definitions selectable on loops.</summary>
        [JsonProperty("wires")]
        public List<WireConfig> Wires { get; set; } = new List<WireConfig>();

        /// <summary>
        /// Per-module parameter mappings and category settings, keyed by module ID.
        /// Mirrors what is stored in Revit Extensible Storage, providing a machine-wide
        /// fallback so settings survive across documents and fresh models.
        /// </summary>
        [JsonProperty("moduleSettings")]
        public Dictionary<string, ModuleSettings> ModuleSettings { get; set; } = new Dictionary<string, ModuleSettings>();

        /// <summary>User-defined paper sizes available in the diagram canvas paper-size selector.</summary>
        [JsonProperty("paperSizes")]
        public List<PaperSizeConfig> PaperSizes { get; set; } = new List<PaperSizeConfig>();
    }
}
