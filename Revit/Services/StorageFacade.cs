using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Pulse.Core.Boq;
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

        // Save BOQ settings (writes to ES)
        private readonly SaveBoqSettingsHandler _saveBoqSettingsHandler;
        private readonly ExternalEvent _saveBoqSettingsEvent;

        // Pick element from Revit viewport
        private readonly PickElementHandler _pickElementHandler;
        private readonly ExternalEvent _pickElementEvent;

        // Draw wire routing visualisation (model lines)
        private readonly DrawWireRoutingHandler _drawWireHandler;
        private readonly ExternalEvent _drawWireEvent;

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

            _saveBoqSettingsHandler = new SaveBoqSettingsHandler();
            _saveBoqSettingsEvent = ExternalEvent.Create(_saveBoqSettingsHandler);

            _pickElementHandler = new PickElementHandler();
            _pickElementEvent = ExternalEvent.Create(_pickElementHandler);

            _drawWireHandler = new DrawWireRoutingHandler();
            _drawWireEvent = ExternalEvent.Create(_drawWireHandler);
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

        /// <summary>
        /// Read BOQ settings from ES. Returns null if not yet saved.
        /// Must be called from the Revit API thread.
        /// </summary>
        public BoqSettings ReadBoqSettings(Document doc)
        {
            if (doc == null) return null;
            try
            {
                return new ExtensibleStorageService(doc, _logger).ReadBoqSettings();
            }
            catch (Exception ex)
            {
                _logger.Error("StorageFacade.ReadBoqSettings failed.", ex);
                return null;
            }
        }

        // ─── Synchronous write helpers (call from the Revit API thread only) ────

        /// <summary>
        /// Synchronously write topology assignments to ES.
        /// Must only be called from the Revit API thread (e.g. inside IExternalCommand.Execute).
        /// Used to flush in-memory state before a new MainViewModel reads it back.
        /// </summary>
        public void SyncWriteTopologyAssignments(Document doc, TopologyAssignmentsStore store)
        {
            if (doc == null || store == null) return;
            try { new ExtensibleStorageService(doc, _logger).WriteTopologyAssignments(store); }
            catch (Exception ex) { _logger.Error("SyncWriteTopologyAssignments failed.", ex); }
        }

        /// <summary>
        /// Synchronously write diagram visibility settings to ES.
        /// Must only be called from the Revit API thread (e.g. inside IExternalCommand.Execute).
        /// </summary>
        public void SyncWriteDiagramSettings(Document doc, LevelVisibilitySettings settings)
        {
            if (doc == null || settings == null) return;
            try { new ExtensibleStorageService(doc, _logger).WriteDiagramSettings(settings); }
            catch (Exception ex) { _logger.Error("SyncWriteDiagramSettings failed.", ex); }
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

        /// <summary>
        /// Save BOQ settings to ES via ExternalEvent (safe to call from WPF thread).
        /// </summary>
        public void SaveBoqSettings(BoqSettings settings, Action onSaved = null, Action<Exception> onError = null)
        {
            _saveBoqSettingsHandler.Settings = settings;
            _saveBoqSettingsHandler.OnSaved  = onSaved;
            _saveBoqSettingsHandler.OnError  = onError;
            _saveBoqSettingsEvent.Raise();
        }

        /// <summary>
        /// Lets the user pick a single element in the Revit viewport.
        /// Minimise the plugin window before calling; restore it in the callbacks.
        /// </summary>
        public void PickElement(
            string prompt,
            Action<long> onPicked,
            Action onCancelled = null,
            Action<Exception> onError = null)
        {
            _pickElementHandler.PromptMessage = prompt ?? "Select an element";
            _pickElementHandler.OnPicked      = onPicked;
            _pickElementHandler.OnCancelled   = onCancelled;
            _pickElementHandler.OnError       = onError;
            _pickElementEvent.Raise();
        }
        /// <summary>
        /// Draw (or clear) Manhattan-routed model lines for a single loop.
        /// </summary>
        public void DrawWireRouting(
            string loopKey,
            List<(double X, double Y, double Z)> waypoints,
            bool clearOnly = false,
            Action<int> onCompleted = null,
            Action<Exception> onError = null)
        {
            _drawWireHandler.LoopKey = loopKey;
            _drawWireHandler.Waypoints = waypoints;
            _drawWireHandler.ClearOnly = clearOnly;
            _drawWireHandler.OnCompleted = onCompleted;
            _drawWireHandler.OnError = onError;
            _drawWireEvent.Raise();
        }
    }
}
