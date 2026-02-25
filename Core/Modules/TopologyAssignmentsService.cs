using System;
using Autodesk.Revit.DB;
using Pulse.Core.Settings;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Owns the in-memory <see cref="TopologyAssignmentsStore"/> lifecycle:
    ///   - initial load from Extensible Storage
    ///   - provides the current store for consumers (Topology, Diagram, Inspector)
    ///   - persists via a supplied save callback (StorageFacade.SaveTopologyAssignments)
    ///   - synchronous flush for re-entry scenarios (FlushToRevit)
    ///
    /// Extracted from MainViewModel so the ViewModel only holds a reference.
    /// </summary>
    public sealed class TopologyAssignmentsService
    {
        private TopologyAssignmentsStore _store;

        /// <summary>Current in-memory assignments (never null).</summary>
        public TopologyAssignmentsStore Store => _store;

        /// <summary>
        /// Callback used to persist the current store asynchronously to
        /// Revit Extensible Storage via <c>StorageFacade.SaveTopologyAssignments</c>.
        /// Set once during MainViewModel construction.
        /// </summary>
        public Action<TopologyAssignmentsStore> SaveRequested { get; set; }

        /// <summary>
        /// Callback for synchronous ES writes (used during FlushToRevit).
        /// Signature: (Document doc, TopologyAssignmentsStore store).
        /// </summary>
        public Action<Document, TopologyAssignmentsStore> SyncWriteRequested { get; set; }

        public TopologyAssignmentsService()
        {
            _store = new TopologyAssignmentsStore();
        }

        /// <summary>
        /// Replace the in-memory store with one loaded from Extensible Storage.
        /// Called once during startup while on the Revit API thread.
        /// </summary>
        public void Load(TopologyAssignmentsStore store)
        {
            _store = store ?? new TopologyAssignmentsStore();
        }

        /// <summary>
        /// Trigger an asynchronous persist of the current store.
        /// Delegates to <see cref="SaveRequested"/>.
        /// </summary>
        public void RequestSave()
        {
            SaveRequested?.Invoke(_store);
        }

        /// <summary>
        /// Synchronously flush the current store to Extensible Storage.
        /// Must only be called from the Revit API thread (e.g. IExternalCommand.Execute).
        /// </summary>
        public void FlushToRevit(Document doc)
        {
            if (doc == null) return;
            SyncWriteRequested?.Invoke(doc, _store);
        }
    }
}
