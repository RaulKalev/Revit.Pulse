using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Revit.ExternalEvents;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Manages the active module, its settings, and orchestrates refresh cycles.
    /// Extracted from MainViewModel to separate platform orchestration from UI binding.
    /// </summary>
    public class PulseAppController
    {
        private readonly List<IModuleDefinition> _modules = new List<IModuleDefinition>();
        private IModuleDefinition _activeModule;
        private ModuleSettings _activeSettings;
        private ModuleData _currentData;

        /// <summary>Raised when the active module changes.</summary>
        public event Action<IModuleDefinition> ActiveModuleChanged;

        /// <summary>Raised when new data is collected successfully.</summary>
        public event Action<ModuleData> DataCollected;

        /// <summary>Raised when a refresh cycle errors.</summary>
        public event Action<Exception> RefreshError;

        /// <summary>Raised when refresh starts.</summary>
        public event Action RefreshStarted;

        /// <summary>All registered module definitions.</summary>
        public IReadOnlyList<IModuleDefinition> Modules => _modules;

        /// <summary>Currently active module definition.</summary>
        public IModuleDefinition ActiveModule => _activeModule;

        /// <summary>Current module settings (parameter mappings, categories).</summary>
        public ModuleSettings ActiveSettings
        {
            get => _activeSettings;
            set => _activeSettings = value;
        }

        /// <summary>Latest collected module data (null until first refresh).</summary>
        public ModuleData CurrentData => _currentData;

        /// <summary>
        /// Register a module definition. The first registered module becomes active.
        /// </summary>
        public void RegisterModule(IModuleDefinition module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            _modules.Add(module);
            if (_activeModule == null)
            {
                _activeModule = module;
                _activeSettings = module.GetDefaultSettings();
            }
        }

        /// <summary>
        /// Switch the active module by ModuleId.
        /// </summary>
        public bool SetActiveModule(string moduleId)
        {
            var match = _modules.Find(m =>
                string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return false;
            _activeModule = match;
            _activeSettings = match.GetDefaultSettings();
            ActiveModuleChanged?.Invoke(match);
            return true;
        }

        /// <summary>
        /// Apply new settings from a dialog save.
        /// </summary>
        public void ApplySettings(ModuleSettings settings)
        {
            _activeSettings = settings;
        }

        /// <summary>
        /// Record data produced by a successful refresh cycle.
        /// Fires <see cref="DataCollected"/>.
        /// </summary>
        public void OnRefreshCompleted(ModuleData data)
        {
            _currentData = data;
            DataCollected?.Invoke(data);
        }

        /// <summary>
        /// Record a refresh error. Fires <see cref="RefreshError"/>.
        /// </summary>
        public void OnRefreshFailed(Exception ex)
        {
            RefreshError?.Invoke(ex);
        }

        /// <summary>
        /// Signal that a refresh cycle is starting. Fires <see cref="RefreshStarted"/>.
        /// </summary>
        public void OnRefreshStarting()
        {
            RefreshStarted?.Invoke();
        }

        // ── Capability queries ───────────────────────────────────────────────

        /// <summary>
        /// Check whether the active module declares a specific capability.
        /// Returns false if no module is active.
        /// </summary>
        public bool HasCapability(ModuleCapabilities cap)
        {
            return _activeModule != null && (_activeModule.Capabilities & cap) == cap;
        }

        /// <summary>
        /// Try to obtain a feature interface from the active module.
        /// Returns null if the module does not implement <typeparamref name="T"/>.
        /// </summary>
        public T GetFeature<T>() where T : class
        {
            return _activeModule as T;
        }
    }
}
