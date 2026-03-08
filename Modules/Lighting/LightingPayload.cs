using System.Collections.Generic;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Module-specific payload for the Lighting module.
    /// Attached to <see cref="Pulse.Core.Modules.ModuleData.Payload"/> by
    /// <see cref="LightingCollector"/> and consumed by topology builders, rules,
    /// BOQ providers, and (via cast) by UI ViewModels.
    ///
    /// Access via <c>data.GetPayload&lt;LightingPayload&gt;()</c>.
    /// Returns null for any non-Lighting <see cref="Pulse.Core.Modules.ModuleData"/>.
    ///
    /// Terminology mapping to Pulse generic entities:
    ///   Controller → Panel (top-level container)
    ///   Line       → Loop  (signalling channel within a controller)
    ///   Luminaire  → AddressableDevice (leaf entity)
    /// </summary>
    public sealed class LightingPayload
    {
        /// <summary>All lighting controllers discovered by the collector.</summary>
        public List<Panel> Controllers { get; } = new List<Panel>();

        /// <summary>All lighting lines/channels discovered by the collector.</summary>
        public List<Loop> Lines { get; } = new List<Loop>();

        /// <summary>All lighting devices (luminaires) discovered by the collector.</summary>
        public List<AddressableDevice> Devices { get; } = new List<AddressableDevice>();

        /// <summary>All zones discovered by the collector (may be empty).</summary>
        public List<Zone> Zones { get; } = new List<Zone>();
    }
}
