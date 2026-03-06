using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Pulse.Core.Settings;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Marker interface for a module's hardware device configuration.
    /// Each module that provides configurable hardware types (panels, circuits, wires, etc.)
    /// implements this interface and registers via <see cref="Pulse.Core.Modules.IProvidesDeviceConfig"/>.
    /// The config is persisted in <see cref="DeviceConfigStore.ModuleConfigBlobs"/> under the module ID.
    /// </summary>
    public interface IModuleDeviceConfig
    {
        /// <summary>The module ID this config belongs to (e.g. "FireAlarm").</summary>
        string ModuleId { get; }
    }

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

        /// <summary>
        /// Total maximum device addresses supported by the panel across all loops.
        /// When set to a value greater than zero this overrides the derived value
        /// (AddressesPerLoop × loop count) for capacity gauges and metrics.
        /// Set to 0 to use the automatic calculation.
        /// </summary>
        [JsonProperty("maxAddresses")]
        public int MaxAddresses { get; set; } = 0;

        // ── Battery / PSU fields (EN 54-4 / NFPA 72) ──────────────────────────

        /// <summary>
        /// Capacity of one individual battery unit in Amp-hours (Ah).
        /// Set to 0 (default) to skip the battery check for this panel.
        /// </summary>
        [JsonProperty("batteryUnitAh")]
        public double BatteryUnitAh { get; set; } = 0.0;

        // Backward compat: JSON saved before rename still uses "batteryCapacityAh".
        [JsonProperty("batteryCapacityAh")]
        private double LegacyBatteryCapacityAh { set { if (BatteryUnitAh <= 0) BatteryUnitAh = value; } }

        /// <summary>
        /// PSU rated output current in Amperes.
        /// Set to 0 (default) to skip the PSU output-current sufficiency check.
        /// </summary>
        [JsonProperty("psuOutputCurrentA")]
        public double PsuOutputCurrentA { get; set; } = 0.0;

        /// <summary>
        /// Required standby duration in hours (EN 54-4 Type C = 24 h, NFPA 72 = 24 h).
        /// </summary>
        [JsonProperty("requiredStandbyHours")]
        public double RequiredStandbyHours { get; set; } = 24.0;

        /// <summary>
        /// Required full-alarm duration in minutes (EN 54-4 = 30 min, NFPA 72 = 5 min).
        /// </summary>
        [JsonProperty("requiredAlarmMinutes")]
        public double RequiredAlarmMinutes { get; set; } = 30.0;

        /// <summary>
        /// Safety factor applied to the calculated required capacity (e.g. 1.25 = 25 % headroom).
        /// Common design-practice margin; set to 1.0 to disable.
        /// </summary>
        [JsonProperty("batterySafetyFactor")]
        public double BatterySafetyFactor { get; set; } = 1.25;
    }

    /// <summary>
    /// Configuration record for an ancillary NAC/sounder Power Supply Unit (PSU).
    /// Used to check battery standby duration and output-current sufficiency for
    /// SubCircuits assigned to this PSU.
    /// </summary>
    public class PsuConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "New PSU";

        /// <summary>
        /// Nominal output voltage of this PSU in Volts (e.g. 24 V for EN 54-4).
        /// </summary>
        [JsonProperty("voltageV")]
        public double VoltageV { get; set; } = 24.0;

        /// <summary>
        /// Capacity of one individual battery unit in Amp-hours (Ah).
        /// Set to 0 (default) to skip the battery check for this PSU.
        /// </summary>
        [JsonProperty("batteryUnitAh")]
        public double BatteryUnitAh { get; set; } = 0.0;

        // Backward compat: JSON saved before rename still uses "batteryCapacityAh".
        [JsonProperty("batteryCapacityAh")]
        private double LegacyBatteryCapacityAhPsu { set { if (BatteryUnitAh <= 0) BatteryUnitAh = value; } }

        /// <summary>
        /// PSU rated output current in Amperes.
        /// Set to 0 (default) to skip the output-current sufficiency check.
        /// </summary>
        [JsonProperty("outputCurrentA")]
        public double OutputCurrentA { get; set; } = 0.0;

        /// <summary>
        /// Required standby duration in hours (EN 54-4 Type C = 24 h, NFPA 72 = 24 h).
        /// </summary>
        [JsonProperty("requiredStandbyHours")]
        public double RequiredStandbyHours { get; set; } = 24.0;

        /// <summary>
        /// Required full-alarm duration in minutes (EN 54-4 = 30 min, NFPA 72 = 5 min).
        /// </summary>
        [JsonProperty("requiredAlarmMinutes")]
        public double RequiredAlarmMinutes { get; set; } = 30.0;

        /// <summary>
        /// Safety factor applied to the calculated required capacity (e.g. 1.25 = 25 % headroom).
        /// </summary>
        [JsonProperty("batterySafetyFactor")]
        public double BatterySafetyFactor { get; set; } = 1.25;
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

        /// <summary>
        /// Measured resistance per metre for a single conductor (Ω/m) at 20 °C.
        /// When non-zero this value is used directly instead of deriving resistance
        /// from copper resistivity × cross-section area, giving datasheet accuracy.
        /// Leave as 0 to use the calculated value.
        /// </summary>
        [JsonProperty("resistancePerMetreOhm")]
        public double ResistancePerMetreOhm { get; set; } = 0.0;

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

        /// <summary>
        /// Opaque per-module hardware config blobs, keyed by module ID.
        /// Each module serialises its own typed config (e.g. <c>FireAlarmDeviceConfig</c>)
        /// into this dictionary so the shared store remains module-agnostic.
        /// New modules write here instead of adding top-level fields.
        /// </summary>
        [JsonProperty("moduleConfigBlobs")]
        public Dictionary<string, string> ModuleConfigBlobs { get; set; } = new Dictionary<string, string>();
    }
}
