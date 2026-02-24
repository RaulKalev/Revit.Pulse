using System.Collections.Generic;
using Pulse.Core.Graph;
using Pulse.Core.Rules;
using Pulse.Core.SystemModel;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Container for all data collected and built by a module.
    /// Holds the topology graph, typed entities, and rule results.
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

        /// <summary>All panels discovered by the collector.</summary>
        public List<Panel> Panels { get; } = new List<Panel>();

        /// <summary>All loops discovered by the collector.</summary>
        public List<Loop> Loops { get; } = new List<Loop>();

        /// <summary>All zones discovered by the collector (may be empty in MVP).</summary>
        public List<Zone> Zones { get; } = new List<Zone>();

        /// <summary>All devices discovered by the collector.</summary>
        public List<AddressableDevice> Devices { get; } = new List<AddressableDevice>();

        /// <summary>All Revit levels in the project, ordered by elevation ascending.</summary>
        public List<LevelInfo> Levels { get; } = new List<LevelInfo>();

        /// <summary>
        /// All rule validation results. Populated after running IRulePack.
        /// </summary>
        public List<RuleResult> RuleResults { get; } = new List<RuleResult>();

        /// <summary>Total number of warnings (Severity.Warning or Error).</summary>
        public int WarningCount => RuleResults.FindAll(r => r.Severity >= Severity.Warning).Count;

        /// <summary>Total number of errors (Severity.Error only).</summary>
        public int ErrorCount => RuleResults.FindAll(r => r.Severity == Severity.Error).Count;
    }
}
