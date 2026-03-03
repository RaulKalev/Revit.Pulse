using System;
using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Represents a one-way NAC (Notification Appliance Circuit) branch attached to
    /// an Output Module on a loop.  Unlike a loop, a SubCircuit is not bi-directional
    /// and is not closed — it is a single cable run driving sounder/NAC devices.
    ///
    /// SubCircuits are persisted inside <see cref="Pulse.Core.Settings.TopologyAssignmentsStore"/>
    /// and projected into the topology graph as child nodes of their host element.
    /// </summary>
    public class SubCircuit
    {
        /// <summary>Stable GUID-based identifier for this SubCircuit.</summary>
        public string Id { get; set; }

        /// <summary>
        /// Revit ElementId of the host Output Module element.
        /// Stored as <see langword="int"/> so it survives JSON round-trips without precision loss.
        /// </summary>
        public int HostElementId { get; set; }

        /// <summary>User-visible name (e.g. "NAC-01").</summary>
        public string Name { get; set; }

        /// <summary>
        /// Revit ElementIds of the sounder/NAC devices assigned to this SubCircuit.
        /// Order is not semantically significant.
        /// </summary>
        public List<int> DeviceElementIds { get; set; } = new List<int>();

        /// <summary>
        /// Optional wire-type key (matches a wire name in DeviceConfigStore.Wires).
        /// Null/empty means unassigned.
        /// </summary>
        public string WireTypeKey { get; set; }

        public SubCircuit() { }

        public SubCircuit(string id, int hostElementId, string name)
        {
            Id            = id            ?? throw new ArgumentNullException(nameof(id));
            HostElementId = hostElementId;
            Name          = name          ?? throw new ArgumentNullException(nameof(name));
        }

        public override string ToString() => $"SubCircuit: {Name} (host={HostElementId})";
    }
}
