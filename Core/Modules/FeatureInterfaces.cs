using Pulse.Core.Settings;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Optional service interface for modules that provide wiring diagram features.
    /// If a module declares <see cref="ModuleCapabilities.Diagram"/>, it should be
    /// able to supply data that the diagram panel can render. This interface is a
    /// marker for now; the actual data flows through <see cref="ModuleData"/> and
    /// <see cref="TopologyAssignmentsStore"/>. Future evolution can add methods here.
    /// </summary>
    public interface IProvidesDiagramFeatures
    {
        // Marker — future methods:
        // IReadOnlyList<PanelInfo> BuildDiagramModel(ModuleData data, DeviceConfigStore config);
    }

    /// <summary>
    /// Optional service interface for modules that support wire-type assignment.
    /// If a module declares <see cref="ModuleCapabilities.Wiring"/>, it can provide
    /// wire config names and write wire assignments back to Revit parameters.
    /// </summary>
    public interface IProvidesWiringFeatures
    {
        /// <summary>
        /// The logical parameter key that maps to the Revit parameter holding wire info.
        /// For Fire Alarm this is <c>FireAlarmParameterKeys.Wire</c>.
        /// </summary>
        string WireParameterKey { get; }
    }

    /// <summary>
    /// Optional service interface for modules that support per-device-type symbol mapping.
    /// If a module declares <see cref="ModuleCapabilities.SymbolMapping"/>, it manages
    /// a mapping from device type strings to custom symbol IDs.
    /// </summary>
    public interface IProvidesSymbolMapping
    {
        // Marker — mapping data currently lives in TopologyAssignmentsStore.SymbolMappings.
        // Future methods:
        // IReadOnlyDictionary<string, string> GetDefaultSymbolMappings();
    }
}
