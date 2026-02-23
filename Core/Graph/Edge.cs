using System;

namespace Pulse.Core.Graph
{
    /// <summary>
    /// Represents a directed edge in the system topology graph.
    /// Edges connect parent nodes to child nodes (e.g., Panel -> Loop -> Device).
    /// </summary>
    public class Edge
    {
        /// <summary>Id of the source (parent) node.</summary>
        public string SourceId { get; }

        /// <summary>Id of the target (child) node.</summary>
        public string TargetId { get; }

        /// <summary>
        /// Optional label for the relationship (e.g., "contains", "assigned-to").
        /// </summary>
        public string Relationship { get; set; }

        public Edge(string sourceId, string targetId, string relationship = null)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
            Relationship = relationship;
        }

        public override string ToString() => $"{SourceId} -> {TargetId}";
    }
}
