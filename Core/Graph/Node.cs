using System;
using System.Collections.Generic;

namespace Pulse.Core.Graph
{
    /// <summary>
    /// Represents a node in the system topology graph.
    /// Each node corresponds to a system entity (Panel, Loop, Zone, Device).
    /// </summary>
    public class Node
    {
        /// <summary>Unique identifier for this node within the graph.</summary>
        public string Id { get; }

        /// <summary>Display label shown in the topology view.</summary>
        public string Label { get; set; }

        /// <summary>
        /// Type category of this node (e.g., "Panel", "Loop", "Zone", "Device").
        /// Used for visual styling and filtering.
        /// </summary>
        public string NodeType { get; set; }

        /// <summary>
        /// Optional Revit ElementId — stored as long to keep Core free from Revit types.
        /// Null for virtual grouping nodes that have no Revit element.
        /// </summary>
        public long? RevitElementId { get; set; }

        /// <summary>
        /// Arbitrary key-value metadata extracted from Revit parameters.
        /// Keys are the logical parameter names from the mapping; values are strings.
        /// </summary>
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of warnings associated with this node.</summary>
        public int WarningCount { get; set; }

        public Node(string id, string label, string nodeType)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? id;
            NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        }

        public override string ToString() => $"{NodeType}: {Label}";
    }
}
