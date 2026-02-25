using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Pulse.Core.Logging;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Revit.ExternalEvents;
using Pulse.Revit.Storage;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Wraps <see cref="ExtensibleStorageService"/> with safe-read/write helpers
    /// and owns the ExternalEvent handlers that persist data to Revit ES.
    /// Extracted from MainViewModel so storage operations are centralised.
    ///
    /// Read operations can execute on the Revit API thread directly.
    /// Write operations must go through ExternalEvent handlers.
    /// </summary>
    public class StorageFacade
    {
        // Save-settings handler (writes to JSON file, not ES)
        private readonly SaveSettingsHandler _saveSettingsHandler;
        private readonly ExternalEvent _saveSettingsEvent;

        // Write-parameter handler
        private readonly WriteParameterHandler _writeParamHandler;
        private readonly ExternalEvent _writeParamEvent;

        // Save diagram visibility (writes to ES)
        private readonly SaveDiagramSettingsHandler _saveDiagramHandler;
        private readonly ExternalEvent _saveDiagramEvent;

        // Save topology assignments (writes to ES)
        private readonly SaveTopologyAssignmentsHandler _saveAssignmentsHandler;
        private readonly ExternalEvent _saveAssignmentsEvent;

        private readonly ILogger _logger;

        public StorageFacade(ILogger logger = null)
        {
            _logger = logger ?? new DebugLogger("Pulse.Storage");

            _saveSettingsHandler = new SaveSettingsHandler();
            _saveSettingsEvent = ExternalEvent.Create(_saveSettingsHandler);

            _writeParamHandler = new WriteParameterHandler();
            _writeParamEvent = ExternalEvent.Create(_writeParamHandler);

            _saveDiagramHandler = new SaveDiagramSettingsHandler();
            _saveDiagramEvent = ExternalEvent.Create(_saveDiagramHandler);

            _saveAssignmentsHandler = new SaveTopologyAssignmentsHandler();
            _saveAssignmentsEvent = ExternalEvent.Create(_saveAssignmentsHandler);
        }

        // ─── Read helpers (call from Revit API thread or construction) ───────

        /// <summary>
        /// Read all settings from a Revit document's Extensible Storage.
        /// Safe to call from the Revit API thread (e.g. during IExternalCommand.Execute).
        /// Returns null on any failure.
        /// </summary>
        public Dictionary<string, ModuleSettings> ReadSettings(Document doc)
        {
            if (doc == null) return null;
            try
            {
                return new ExtensibleStorageService(doc, _logger).ReadSettings();
            }
            catch (Exception ex)
            {
                _logger.Error("StorageFacade.ReadSettings failed.", ex);
                return null;
            }
        }

        /// <summary>
        /// Read diagram visibility settings from ES. Returns null on failure.
        /// </summary>
        public LevelVisibilitySettings ReadDiagramSettings(Document doc)
        {
            if (doc == null) return null;
            try
            {
                return new ExtensibleStorageService(doc, _logger).ReadDiagramSettings();
            }
            catch (Exception ex)
            {
                _logger.Error("StorageFacade.ReadDiagramSettings failed.", ex);
                return null;
            }
        }

        /// <summary>
        /// Read topology assignments from ES. Returns empty store on failure.
        /// </summary>
        public TopologyAssignmentsStore ReadTopologyAssignments(Document doc)
        {
            if (doc == null) return new TopologyAssignmentsStore();
            try
            {
                return new ExtensibleStorageService(doc, _logger).ReadTopologyAssignments();
            }
            catch (Exception ex)
            {
                _logger.Error("StorageFacade.ReadTopologyAssignments failed.", ex);
                return new TopologyAssignmentsStore();
            }
        }

        // ─── Write helpers (raise ExternalEvents) ───────────────────────────

        /// <summary>
        /// Save module settings to the JSON file (device-config.json).
        /// Runs on the Revit thread via ExternalEvent.
        /// </summary>
        public void SaveSettings(ModuleSettings settings, Action onSaved = null, Action<Exception> onError = null)
        {
            _saveSettingsHandler.Settings = settings;
            _saveSettingsHandler.OnSaved = onSaved;
            _saveSettingsHandler.OnError = onError;
            _saveSettingsEvent.Raise();
        }

        /// <summary>
        /// Write parameter values to Revit elements via transaction.
        /// </summary>
        public void WriteParameters(
            List<(long ElementId, string ParameterName, string Value)> writes,
            Action<int> onCompleted = null,
            Action<Exception> onError = null)
        {
            _writeParamHandler.Writes = writes;
            _writeParamHandler.OnCompleted = onCompleted;
            _writeParamHandler.OnError = onError;
            _writeParamEvent.Raise();
        }

        /// <summary>
        /// Save diagram visibility settings to ES.
        /// </summary>
        public void SaveDiagramSettings(LevelVisibilitySettings settings)
        {
            _saveDiagramHandler.Settings = settings;
            _saveDiagramEvent.Raise();
        }

        /// <summary>
        /// Save topology assignments to ES.
        /// </summary>
        public void SaveTopologyAssignments(TopologyAssignmentsStore store)
        {
            _saveAssignmentsHandler.Store = store;
            _saveAssignmentsEvent.Raise();
        }
    }
}
