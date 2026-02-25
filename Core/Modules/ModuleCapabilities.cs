using System;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Flags representing optional capabilities a module may provide.
    /// Used for runtime feature discovery without tight coupling.
    /// Default value is <see cref="None"/>; modules declare their capabilities
    /// at registration time.
    ///
    /// All flags that were previously "always-on" behaviour are kept active
    /// by default in existing modules so there is no change in runtime behaviour.
    /// Future modules can omit flags they do not support.
    /// </summary>
    [Flags]
    public enum ModuleCapabilities
    {
        /// <summary>No optional capabilities.</summary>
        None = 0,

        /// <summary>Module supports the interactive wiring diagram panel.</summary>
        Diagram = 1 << 0,

        /// <summary>Module supports wire-type assignment on loops.</summary>
        Wiring = 1 << 1,

        /// <summary>Module supports custom symbol mapping per device type.</summary>
        SymbolMapping = 1 << 2,

        /// <summary>Module supports capacity gauges (address count + current draw).</summary>
        CapacityGauges = 1 << 3,

        /// <summary>Module supports topology config assignments (panel/loop configs).</summary>
        ConfigAssignment = 1 << 4,

        /// <summary>Convenience: all capabilities enabled (used by Fire Alarm module).</summary>
        All = Diagram | Wiring | SymbolMapping | CapacityGauges | ConfigAssignment
    }
}
