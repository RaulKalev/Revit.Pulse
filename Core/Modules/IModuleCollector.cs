using Pulse.Core.Settings;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Collects raw element data from Revit and populates ModuleData with typed entities.
    /// Implementations are module-specific (e.g., FireAlarmCollector).
    /// </summary>
    public interface IModuleCollector
    {
        /// <summary>
        /// Collect elements from the active Revit document using the provided settings.
        /// Populates the Devices, Panels, Loops, and Zones lists in ModuleData.
        /// 
        /// This method receives a collector service abstraction to avoid direct Revit API coupling.
        /// The actual Revit API calls happen in the Revit layer.
        /// </summary>
        /// <param name="collectorContext">Provides access to Revit element data.</param>
        /// <param name="settings">The current module configuration (categories, parameter mapping).</param>
        /// <returns>Populated ModuleData containing all discovered entities.</returns>
        ModuleData Collect(ICollectorContext collectorContext, ModuleSettings settings);
    }

    /// <summary>
    /// Abstraction over Revit element collection.
    /// Implemented in the Revit layer to decouple Core from RevitAPI types.
    /// </summary>
    public interface ICollectorContext
    {
        /// <summary>
        /// Get all elements from the specified Revit category.
        /// Returns a list of element data bags (id + parameter values).
        /// </summary>
        /// <param name="categoryName">The Revit category name (e.g., "Fire Alarm Devices").</param>
        /// <param name="parameterNames">The Revit parameter names to extract.</param>
        System.Collections.Generic.IReadOnlyList<ElementData> GetElements(string categoryName, System.Collections.Generic.IReadOnlyList<string> parameterNames);
    }

    /// <summary>
    /// Lightweight data bag representing a single Revit element with extracted parameters.
    /// Decouples Core from Autodesk.Revit.DB types.
    /// </summary>
    public class ElementData
    {
        /// <summary>Revit ElementId as long.</summary>
        public long ElementId { get; set; }

        /// <summary>
        /// Parameter values keyed by Revit parameter name.
        /// Values are always strings (converted at extraction time).
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Parameters { get; set; }
            = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
