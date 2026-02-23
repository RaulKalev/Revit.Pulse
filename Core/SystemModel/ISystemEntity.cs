using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Base contract for all addressable system entities.
    /// Implemented by Panel, Loop, Zone, AddressableDevice.
    /// </summary>
    public interface ISystemEntity
    {
        /// <summary>Unique identifier within the module scope.</summary>
        string EntityId { get; }

        /// <summary>Display name shown in UI.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Type discriminator — "Panel", "Loop", "Zone", "Device".
        /// </summary>
        string EntityType { get; }

        /// <summary>
        /// Optional Revit ElementId — stored as long to avoid coupling to Revit types.
        /// Null for virtual / grouping entities.
        /// </summary>
        long? RevitElementId { get; }

        /// <summary>
        /// Key-value properties extracted from Revit parameters.
        /// Keys use the logical names from the parameter mapping.
        /// </summary>
        IReadOnlyDictionary<string, string> Properties { get; }
    }
}
