using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

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

        // ── SubCircuit CRUD ──────────────────────────────────────────────────────────

        /// <summary>
        /// Create a new SubCircuit attached to <paramref name="hostElementId"/> and optionally
        /// pre-populate it with <paramref name="deviceIds"/>.
        /// Enforces uniqueness: any device already in another SubCircuit is silently skipped.
        /// Returns the new <see cref="SubCircuit"/>.
        /// </summary>
        public SubCircuit CreateSubCircuit(int hostElementId, List<int> deviceIds = null, string name = null)
        {
            EnsureSubCircuitCollections();

            // Auto-generate a name of the form NAC-01, NAC-02 …
            if (string.IsNullOrWhiteSpace(name))
                name = GenerateSubCircuitName(hostElementId);

            var sc = new SubCircuit(Guid.NewGuid().ToString("N"), hostElementId, name);

            // Add devices — enforce uniqueness across all SubCircuits
            if (deviceIds != null)
            {
                var alreadyOwned = OwnedDeviceElementIds();
                foreach (int id in deviceIds)
                {
                    if (!alreadyOwned.Contains(id))
                        sc.DeviceElementIds.Add(id);
                }
            }

            _store.SubCircuits[sc.Id] = sc;

            if (!_store.SubCircuitIdsByHostElementId.TryGetValue(hostElementId, out var hostList))
                _store.SubCircuitIdsByHostElementId[hostElementId] = hostList = new List<string>();
            hostList.Add(sc.Id);

            return sc;
        }

        /// <summary>
        /// Add <paramref name="deviceIds"/> to an existing SubCircuit.
        /// Devices already owned by any SubCircuit are silently skipped.
        /// Returns false if <paramref name="subCircuitId"/> is not found.
        /// </summary>
        public bool AddDevicesToSubCircuit(string subCircuitId, List<int> deviceIds)
        {
            EnsureSubCircuitCollections();
            if (!_store.SubCircuits.TryGetValue(subCircuitId, out var sc)) return false;
            if (deviceIds == null || deviceIds.Count == 0) return true;

            var alreadyOwned = OwnedDeviceElementIds();
            foreach (int id in deviceIds)
            {
                if (!alreadyOwned.Contains(id))
                    sc.DeviceElementIds.Add(id);
            }
            return true;
        }

        /// <summary>
        /// Remove <paramref name="deviceIds"/> from the specified SubCircuit.
        /// Returns false if <paramref name="subCircuitId"/> is not found.
        /// </summary>
        public bool RemoveDevicesFromSubCircuit(string subCircuitId, List<int> deviceIds)
        {
            EnsureSubCircuitCollections();
            if (!_store.SubCircuits.TryGetValue(subCircuitId, out var sc)) return false;
            if (deviceIds == null || deviceIds.Count == 0) return true;

            foreach (int id in deviceIds)
                sc.DeviceElementIds.Remove(id);
            return true;
        }

        /// <summary>
        /// Permanently delete a SubCircuit and remove it from the host index.
        /// Returns false if the ID is not found.
        /// </summary>
        public bool DeleteSubCircuit(string subCircuitId)
        {
            EnsureSubCircuitCollections();
            if (!_store.SubCircuits.TryGetValue(subCircuitId, out var sc)) return false;

            _store.SubCircuits.Remove(subCircuitId);

            if (_store.SubCircuitIdsByHostElementId.TryGetValue(sc.HostElementId, out var hostList))
            {
                hostList.Remove(subCircuitId);
                if (hostList.Count == 0)
                    _store.SubCircuitIdsByHostElementId.Remove(sc.HostElementId);
            }
            return true;
        }

        /// <summary>
        /// Return all SubCircuits whose host is <paramref name="hostElementId"/>.
        /// Returns an empty list when none exist — never null.
        /// </summary>
        public List<SubCircuit> GetSubCircuitsByHost(int hostElementId)
        {
            EnsureSubCircuitCollections();
            if (!_store.SubCircuitIdsByHostElementId.TryGetValue(hostElementId, out var ids)
                || ids == null || ids.Count == 0)
                return new List<SubCircuit>();

            var result = new List<SubCircuit>(ids.Count);
            foreach (var id in ids)
            {
                if (_store.SubCircuits.TryGetValue(id, out var sc))
                    result.Add(sc);
            }
            return result;
        }

        /// <summary>
        /// Remove every device in <paramref name="deletedElementIds"/> from all SubCircuits.
        /// Call this during a model refresh once deleted elements are detected.
        /// </summary>
        public void PurgeDeletedDevices(IEnumerable<int> deletedElementIds)
        {
            EnsureSubCircuitCollections();
            if (deletedElementIds == null) return;
            var toRemove = new HashSet<int>(deletedElementIds);
            foreach (var sc in _store.SubCircuits.Values)
                sc.DeviceElementIds.RemoveAll(id => toRemove.Contains(id));
        }

        /// <summary>
        /// Remove every SubCircuit whose host element ID matches <paramref name="deletedHostId"/>.
        /// Call this when a host Output Module is deleted from the model.
        /// </summary>
        public void PurgeSubCircuitsForHost(int deletedHostId)
        {
            EnsureSubCircuitCollections();
            if (!_store.SubCircuitIdsByHostElementId.TryGetValue(deletedHostId, out var ids)
                || ids == null) return;

            // Copy list so we can iterate while deleting
            foreach (var id in ids.ToList())
                _store.SubCircuits.Remove(id);

            _store.SubCircuitIdsByHostElementId.Remove(deletedHostId);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        /// <summary>Initialise SubCircuit collections if old JSON deserialized nulls.</summary>
        private void EnsureSubCircuitCollections()
        {
            if (_store.SubCircuits == null)
                _store.SubCircuits = new Dictionary<string, SubCircuit>();
            if (_store.SubCircuitIdsByHostElementId == null)
                _store.SubCircuitIdsByHostElementId = new Dictionary<int, List<string>>();
        }

        /// <summary>Returns the flat set of all device element IDs currently owned by any SubCircuit.</summary>
        private HashSet<int> OwnedDeviceElementIds()
        {
            var set = new HashSet<int>();
            foreach (var sc in _store.SubCircuits.Values)
                foreach (int id in sc.DeviceElementIds)
                    set.Add(id);
            return set;
        }

        /// <summary>
        /// Generate the next available "NAC-NN" name for a given host element.
        /// Inspects existing SubCircuits on that host so names are always unique per host.
        /// </summary>
        private string GenerateSubCircuitName(int hostElementId)
        {
            var existing = GetSubCircuitsByHost(hostElementId)
                           .Select(s => s.Name)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= 99; i++)
            {
                string candidate = $"NAC-{i:D2}";
                if (!existing.Contains(candidate))
                    return candidate;
            }
            return $"NAC-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant()}";
        }
    }
}
