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
    }
}
