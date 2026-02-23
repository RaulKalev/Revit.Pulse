using Pulse.Core.Settings;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Defines a module that can be registered with the Pulse platform.
    /// Each module represents a single MEP system (Fire Alarm, Emergency Lighting, etc.).
    /// </summary>
    public interface IModuleDefinition
    {
        /// <summary>Unique machine-readable identifier (e.g., "FireAlarm").</summary>
        string ModuleId { get; }

        /// <summary>Human-readable display name (e.g., "Fire Alarm").</summary>
        string DisplayName { get; }

        /// <summary>Brief description of the module purpose.</summary>
        string Description { get; }

        /// <summary>Module version string.</summary>
        string Version { get; }

        /// <summary>Returns the default parameter mapping for this module.</summary>
        ModuleSettings GetDefaultSettings();

        /// <summary>Creates the collector instance for this module.</summary>
        IModuleCollector CreateCollector();

        /// <summary>Creates the topology builder instance for this module.</summary>
        ITopologyBuilder CreateTopologyBuilder();

        /// <summary>Creates the rule pack instance for this module.</summary>
        IRulePack CreateRulePack();
    }
}
