using System;
using System.Collections.Generic;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Maps logical parameter names used by a module to actual Revit parameter names.
    /// This allows users to customize which Revit parameters feed into module logic
    /// without modifying any code.
    /// </summary>
    public class ParameterMapping
    {
        /// <summary>
        /// Logical key used internally (e.g., "Panel", "Loop", "Address").
        /// </summary>
        public string LogicalName { get; set; }

        /// <summary>
        /// The actual Revit parameter name in the model (e.g., "FA_Panel", "Aadress").
        /// This is what the user configures to match their model.
        /// </summary>
        public string RevitParameterName { get; set; }

        /// <summary>Whether this parameter is required for the module to function.</summary>
        public bool IsRequired { get; set; }

        /// <summary>Default Revit parameter name (used when resetting to defaults).</summary>
        public string DefaultRevitParameterName { get; set; }

        public ParameterMapping() { }

        public ParameterMapping(string logicalName, string revitParameterName, bool isRequired = false)
        {
            LogicalName = logicalName ?? throw new ArgumentNullException(nameof(logicalName));
            RevitParameterName = revitParameterName;
            DefaultRevitParameterName = revitParameterName;
            IsRequired = isRequired;
        }

        public override string ToString() => $"{LogicalName} -> {RevitParameterName}";
    }
}
