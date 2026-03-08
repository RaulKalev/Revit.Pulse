namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Centralized logical parameter keys for the Lighting module.
    /// These are the internal keys used in code — they map to configurable Revit parameter names
    /// via the ModuleSettings parameter mapping table.
    ///
    /// The first supported protocol is DALI, but keys are named generically so future
    /// lighting systems (Sympolight, KNX, etc.) can reuse the same module infrastructure.
    /// </summary>
    public static class LightingParameterKeys
    {
        /// <summary>Logical key for the controller assignment parameter (e.g. DALI controller name).</summary>
        public const string Controller = "Controller";

        /// <summary>Logical key for the line/channel assignment parameter (e.g. DALI line number).</summary>
        public const string Line = "Line";

        /// <summary>Logical key for the device address within a line.</summary>
        public const string Address = "Address";

        /// <summary>Logical key for the device type / luminaire type parameter.</summary>
        public const string DeviceType = "DeviceType";

        /// <summary>Logical key for the current draw in mA (DALI bus current).</summary>
        public const string CurrentDraw = "CurrentDraw";

        /// <summary>Logical key for the device id parameter.</summary>
        public const string DeviceId = "DeviceId";

        /// <summary>
        /// Logical key whose mapped value is the Revit category name used to look up
        /// controller elements (e.g. "Electrical Equipment").
        /// </summary>
        public const string ControllerElementCategory = "ControllerElementCategory";

        /// <summary>
        /// Logical key for the parameter on controller elements whose value
        /// identifies the controller by name.
        /// </summary>
        public const string ControllerElementNameParam = "ControllerElementNameParam";

        /// <summary>
        /// Logical key for the lighting system / protocol type (e.g. "DALI", "Sympolight").
        /// Optional — when absent, DALI is assumed as the default.
        /// </summary>
        public const string SystemType = "SystemType";
    }
}
