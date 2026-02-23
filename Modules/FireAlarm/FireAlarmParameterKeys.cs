namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Centralized logical parameter keys for the Fire Alarm module.
    /// These are the internal keys used in code — they map to configurable Revit parameter names
    /// via the ModuleSettings parameter mapping table.
    /// 
    /// NEVER use raw Revit parameter name strings in logic.
    /// Always reference these constants and resolve the actual name through ModuleSettings.
    /// </summary>
    public static class FireAlarmParameterKeys
    {
        /// <summary>Logical key for the panel assignment parameter.</summary>
        public const string Panel = "Panel";

        /// <summary>Logical key for the loop assignment parameter.</summary>
        public const string Loop = "Loop";

        /// <summary>Logical key for the device address parameter.</summary>
        public const string Address = "Address";

        /// <summary>Logical key for the device type parameter.</summary>
        public const string DeviceType = "DeviceType";

        /// <summary>Logical key for the current draw parameter.</summary>
        public const string CurrentDraw = "CurrentDraw";

        /// <summary>Logical key for the device id parameter.</summary>
        public const string DeviceId = "DeviceId";

        /// <summary>Logical key for the Revit parameter that receives the assigned control-panel config name.</summary>
        public const string PanelConfig = "PanelConfig";

        /// <summary>Logical key for the Revit parameter that receives the assigned loop-module config name.</summary>
        public const string LoopModuleConfig = "LoopModuleConfig";

        /// <summary>
        /// Logical key whose mapped value is the Revit category name used to look up
        /// panel board elements (e.g. "Electrical Equipment").  Stored as a
        /// ParameterMapping so it is editable in the settings UI like any other value.
        /// </summary>
        public const string PanelElementCategory = "PanelElementCategory";

        /// <summary>
        /// Logical key for the parameter on panel board elements whose value
        /// identifies the panel by name (must match the Panel parameter on devices).
        /// </summary>
        public const string PanelElementNameParam = "PanelElementNameParam";

        /// <summary>
        /// Logical key for the parameter on each fire-alarm device that stores the
        /// integer ElementId of its parent electrical circuit.
        /// </summary>
        public const string CircuitElementId = "CircuitElementId";
    }
}
