using System;
using System.Collections.Generic;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Configuration for a single module instance.
    /// Stored in Extensible Storage per Revit document.
    /// </summary>
    public class ModuleSettings
    {
        /// <summary>Module identifier this settings block belongs to.</summary>
        public string ModuleId { get; set; }

        /// <summary>Schema version — used for safe upgrade paths.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Revit categories to collect elements from.
        /// Users can customize which categories the module scans.
        /// </summary>
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>
        /// Parameter mappings: logical name -> Revit parameter name.
        /// </summary>
        public List<ParameterMapping> ParameterMappings { get; set; } = new List<ParameterMapping>();

        /// <summary>
        /// Lookup a Revit parameter name by its logical key.
        /// Returns null if the logical name is not mapped.
        /// </summary>
        public string GetRevitParameterName(string logicalName)
        {
            var mapping = ParameterMappings.Find(m =>
                string.Equals(m.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
            return mapping?.RevitParameterName;
        }

        /// <summary>
        /// Get all Revit parameter names currently mapped.
        /// </summary>
        public List<string> GetAllRevitParameterNames()
        {
            var names = new List<string>();
            foreach (var mapping in ParameterMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.RevitParameterName))
                {
                    names.Add(mapping.RevitParameterName);
                }
            }
            return names;
        }
    }
}
