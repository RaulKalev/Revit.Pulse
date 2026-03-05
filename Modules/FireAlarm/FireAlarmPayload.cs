using System.Collections.Generic;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Module-specific payload for the Fire Alarm module.
    /// Attached to <see cref="Pulse.Core.Modules.ModuleData.Payload"/> by
    /// <see cref="FireAlarmCollector"/> and consumed by topology builders, rules,
    /// BOQ providers, and (via cast) by UI ViewModels.
    ///
    /// Access via <c>data.GetPayload&lt;FireAlarmPayload&gt;()</c>.
    /// Returns null for any non-FA <see cref="Pulse.Core.Modules.ModuleData"/>.
    /// </summary>
    public sealed class FireAlarmPayload
    {
        /// <summary>All panels discovered by the collector.</summary>
        public List<Panel> Panels { get; } = new List<Panel>();

        /// <summary>All loops discovered by the collector.</summary>
        public List<Loop> Loops { get; } = new List<Loop>();

        /// <summary>All zones discovered by the collector (may be empty).</summary>
        public List<Zone> Zones { get; } = new List<Zone>();

        /// <summary>All devices discovered by the collector.</summary>
        public List<AddressableDevice> Devices { get; } = new List<AddressableDevice>();

        /// <summary>
        /// SubCircuits hydrated from <see cref="Core.Settings.TopologyAssignmentsStore"/>
        /// during the pre-build hook. Empty by default — safe if no SubCircuits exist yet.
        /// </summary>
        public List<SubCircuit> SubCircuits { get; } = new List<SubCircuit>();
    }
}
