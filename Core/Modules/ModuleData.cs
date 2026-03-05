using System.Collections.Generic;
using Pulse.Core.Graph;
using Pulse.Core.Rules;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Container for all data collected and built by a module.
    /// Holds the generic topology graph, Revit levels, rule results, and a
    /// module-specific <see cref="Payload"/> for typed entity collections.
    /// Passed between IModuleCollector, ITopologyBuilder, and IRulePack.
    /// </summary>
    public class ModuleData
    {
        /// <summary>Name of the module that produced this data.</summary>
        public string ModuleName { get; set; }

        /// <summary>All nodes in the topology graph.</summary>
        public List<Node> Nodes { get; } = new List<Node>();

        /// <summary>All edges (relationships) in the topology graph.</summary>
        public List<Edge> Edges { get; } = new List<Edge>();

        /// <summary>
        /// Module-specific typed entity collections.
        /// Set by the module's <see cref="IModuleCollector"/> and consumed by
        /// the topology builder, rules, BOQ providers, and (via cast) UI ViewModels.
        /// Cast with <see cref="GetPayload{T}"/> — returns null for a non-matching module.
        /// </summary>
        public object Payload { get; set; }

        /// <summary>
        /// Returns the <see cref="Payload"/> cast to <typeparamref name="T"/>,
        /// or null if the payload is absent or a different type.
        /// </summary>
        public T GetPayload<T>() where T : class => Payload as T;

        /// <summary>All Revit levels in the project, ordered by elevation ascending.</summary>
        public List<LevelInfo> Levels { get; } = new List<LevelInfo>();

        /// <summary>
        /// Routed wire lengths keyed by the sanitized composite key used as the "Pulse Wire – "
        /// model line subcategory suffix (e.g. "APC - NAC-01" for a SubCircuit, or
        /// "Panel_01 - Loop_01" for a loop).
        /// Populated by <see cref="FireAlarmCollector"/> from model lines drawn by the wire
        /// routing feature.  Maps <c>safeKey → totalLengthMetres</c>.
        /// Empty if no Pulse Wire model lines exist in the document.
        /// </summary>
        public Dictionary<string, double> RoutedWireLengths { get; } = new Dictionary<string, double>(System.StringComparer.Ordinal);
        /// All rule validation results. Populated after running IRulePack.
        /// </summary>
        public List<RuleResult> RuleResults { get; } = new List<RuleResult>();

        /// <summary>Total number of warnings (Severity.Warning or Error).</summary>
        public int WarningCount => RuleResults.FindAll(r => r.Severity >= Severity.Warning).Count;

        /// <summary>Total number of errors (Severity.Error only).</summary>
        public int ErrorCount => RuleResults.FindAll(r => r.Severity == Severity.Error).Count;
    }
}
