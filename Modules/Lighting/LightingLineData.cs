namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Per-line persistent data stored in <see cref="Core.Settings.TopologyAssignmentsStore.ModuleBlobs"/>
    /// under the key <c>"Lighting.Lines"</c> as a JSON dictionary keyed by <c>"controllerName::lineName"</c>.
    /// </summary>
    public sealed class LightingLineData
    {
        public const string DefaultColor = "#FFFF4081";

        /// <summary>ARGB hex color string (e.g. "#FFFF4081") used for both topology dot and Revit highlight.</summary>
        public string ColorHex { get; set; } = DefaultColor;
    }
}
