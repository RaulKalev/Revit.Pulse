using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Owns all SubCircuit CRUD for the Fire Alarm module.
    ///
    /// SubCircuits are persisted as an opaque JSON blob
    /// (<see cref="TopologyAssignmentsStore.SubCircuitsJson"/>) so that
    /// <see cref="Core.Settings.TopologyAssignmentsStore"/> carries no dependency on
    /// the FA-specific <see cref="SubCircuit"/> type.
    ///
    /// The service maintains a lazy in-memory cache of the deserialised dictionary
    /// and a host-element-id index built from it.  The cache is invalidated by calling
    /// <see cref="OnStoreLoaded"/> whenever <see cref="TopologyAssignmentsService.Load"/>
    /// replaces the underlying store (i.e. on every document open).
    ///
    /// Callers that mutate a <see cref="SubCircuit"/> property directly (VDrop, cable
    /// temperature, EOL resistor, wire-type key) must call <see cref="PersistToStore"/>
    /// afterwards so the change is captured in the blob before the next
    /// <see cref="TopologyAssignmentsService.RequestSave"/> call.
    /// </summary>
    public sealed class FireAlarmSubCircuitService
    {
        private readonly TopologyAssignmentsService _assignmentsService;
        private Dictionary<string, SubCircuit> _cache;
        private Dictionary<int, List<string>> _indexByHost;

        public FireAlarmSubCircuitService(TopologyAssignmentsService assignmentsService)
        {
            _assignmentsService = assignmentsService
                ?? throw new ArgumentNullException(nameof(assignmentsService));
        }

        // ── Cache lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Invalidates the in-memory cache.  Must be called immediately after
        /// <see cref="TopologyAssignmentsService.Load"/> replaces the store (e.g. on
        /// document open) so the next access re-deserialises from the new blob.
        /// </summary>
        public void OnStoreLoaded()
        {
            _cache = null;
            _indexByHost = null;
        }

        /// <summary>
        /// Serialises the in-memory cache back to
        /// <see cref="TopologyAssignmentsStore.SubCircuitsJson"/>.
        /// Must be called before <see cref="TopologyAssignmentsService.RequestSave"/>
        /// when a <see cref="SubCircuit"/> property has been mutated directly.
        /// </summary>
        public void PersistToStore()
        {
            if (_cache == null) return;
            _assignmentsService.Store.SubCircuitsJson = JsonConvert.SerializeObject(_cache);
        }

        // ── Read access ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Live read-only view of all SubCircuits keyed by <see cref="SubCircuit.Id"/>.
        /// Never null — returns an empty dictionary when no SubCircuits exist.
        /// </summary>
        public IReadOnlyDictionary<string, SubCircuit> SubCircuits => EnsureCache();

        /// <summary>
        /// Returns the <see cref="SubCircuit"/> for the given ID, or <see langword="null"/>.
        /// </summary>
        public SubCircuit GetSubCircuit(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureCache().TryGetValue(id, out var sc);
            return sc;
        }

        // ── CRUD ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Create a new SubCircuit attached to <paramref name="hostElementId"/> and
        /// optionally pre-populate it with <paramref name="deviceIds"/>.
        /// Enforces uniqueness: any device already in another SubCircuit is silently skipped.
        /// </summary>
        public SubCircuit CreateSubCircuit(int hostElementId, List<int> deviceIds = null, string name = null)
        {
            var cache = EnsureCache();

            if (string.IsNullOrWhiteSpace(name))
                name = GenerateSubCircuitName(hostElementId);

            var sc = new SubCircuit(Guid.NewGuid().ToString("N"), hostElementId, name);

            if (deviceIds != null)
            {
                var alreadyOwned = OwnedDeviceElementIds(cache);
                foreach (int id in deviceIds)
                {
                    if (!alreadyOwned.Contains(id))
                        sc.DeviceElementIds.Add(id);
                }
            }

            cache[sc.Id] = sc;

            if (!_indexByHost.TryGetValue(hostElementId, out var hostList))
                _indexByHost[hostElementId] = hostList = new List<string>();
            hostList.Add(sc.Id);

            PersistToStore();
            return sc;
        }

        /// <summary>
        /// Add <paramref name="deviceIds"/> to an existing SubCircuit.
        /// Devices already owned by any SubCircuit are silently skipped.
        /// Returns <see langword="false"/> if <paramref name="subCircuitId"/> is not found.
        /// </summary>
        public bool AddDevicesToSubCircuit(string subCircuitId, List<int> deviceIds)
        {
            var cache = EnsureCache();
            if (!cache.TryGetValue(subCircuitId, out var sc)) return false;
            if (deviceIds == null || deviceIds.Count == 0) return true;

            var alreadyOwned = OwnedDeviceElementIds(cache);
            foreach (int id in deviceIds)
            {
                if (!alreadyOwned.Contains(id))
                    sc.DeviceElementIds.Add(id);
            }
            PersistToStore();
            return true;
        }

        /// <summary>
        /// Rename the specified SubCircuit.
        /// Returns <see langword="false"/> if <paramref name="subCircuitId"/> is not found.
        /// </summary>
        public bool RenameSubCircuit(string subCircuitId, string newName)
        {
            if (!EnsureCache().TryGetValue(subCircuitId, out var sc)) return false;
            sc.Name = newName ?? string.Empty;
            PersistToStore();
            return true;
        }

        /// <summary>
        /// Remove <paramref name="deviceIds"/> from the specified SubCircuit.
        /// Returns <see langword="false"/> if <paramref name="subCircuitId"/> is not found.
        /// </summary>
        public bool RemoveDevicesFromSubCircuit(string subCircuitId, List<int> deviceIds)
        {
            if (!EnsureCache().TryGetValue(subCircuitId, out var sc)) return false;
            if (deviceIds == null || deviceIds.Count == 0) return true;

            foreach (int id in deviceIds)
                sc.DeviceElementIds.Remove(id);
            PersistToStore();
            return true;
        }

        /// <summary>
        /// Permanently delete a SubCircuit and remove it from the host index.
        /// Returns <see langword="false"/> if the ID is not found.
        /// </summary>
        public bool DeleteSubCircuit(string subCircuitId)
        {
            var cache = EnsureCache();
            if (!cache.TryGetValue(subCircuitId, out var sc)) return false;

            cache.Remove(subCircuitId);

            if (_indexByHost.TryGetValue(sc.HostElementId, out var hostList))
            {
                hostList.Remove(subCircuitId);
                if (hostList.Count == 0)
                    _indexByHost.Remove(sc.HostElementId);
            }
            PersistToStore();
            return true;
        }

        /// <summary>
        /// Return all SubCircuits whose host is <paramref name="hostElementId"/>.
        /// Never null — returns an empty list when none exist.
        /// </summary>
        public List<SubCircuit> GetSubCircuitsByHost(int hostElementId)
        {
            var cache = EnsureCache();
            if (!_indexByHost.TryGetValue(hostElementId, out var ids) || ids == null || ids.Count == 0)
                return new List<SubCircuit>();

            var result = new List<SubCircuit>(ids.Count);
            foreach (var id in ids)
            {
                if (cache.TryGetValue(id, out var sc))
                    result.Add(sc);
            }
            return result;
        }

        /// <summary>
        /// Remove every device in <paramref name="deletedElementIds"/> from all SubCircuits.
        /// Call during a model refresh once deleted elements are detected.
        /// </summary>
        public void PurgeDeletedDevices(IEnumerable<int> deletedElementIds)
        {
            if (deletedElementIds == null) return;
            var toRemove = new HashSet<int>(deletedElementIds);
            var cache = EnsureCache();
            bool dirty = false;
            foreach (var sc in cache.Values)
            {
                int before = sc.DeviceElementIds.Count;
                sc.DeviceElementIds.RemoveAll(id => toRemove.Contains(id));
                if (sc.DeviceElementIds.Count != before) dirty = true;
            }
            if (dirty) PersistToStore();
        }

        /// <summary>
        /// Remove every SubCircuit whose host element ID matches <paramref name="deletedHostId"/>.
        /// Call when a host Output Module is deleted from the model.
        /// </summary>
        public void PurgeSubCircuitsForHost(int deletedHostId)
        {
            var cache = EnsureCache();
            if (!_indexByHost.TryGetValue(deletedHostId, out var ids) || ids == null) return;

            foreach (var id in ids.ToList())
                cache.Remove(id);

            _indexByHost.Remove(deletedHostId);
            PersistToStore();
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        private Dictionary<string, SubCircuit> EnsureCache()
        {
            if (_cache != null) return _cache;

            var json = _assignmentsService.Store.SubCircuitsJson;
            _cache = !string.IsNullOrEmpty(json)
                ? JsonConvert.DeserializeObject<Dictionary<string, SubCircuit>>(json)
                  ?? new Dictionary<string, SubCircuit>()
                : new Dictionary<string, SubCircuit>();

            RebuildIndex();
            return _cache;
        }

        private void RebuildIndex()
        {
            _indexByHost = new Dictionary<int, List<string>>();
            foreach (var kvp in _cache)
            {
                if (!_indexByHost.TryGetValue(kvp.Value.HostElementId, out var list))
                    _indexByHost[kvp.Value.HostElementId] = list = new List<string>();
                list.Add(kvp.Key);
            }
        }

        private static HashSet<int> OwnedDeviceElementIds(Dictionary<string, SubCircuit> cache)
        {
            var set = new HashSet<int>();
            foreach (var sc in cache.Values)
                foreach (int id in sc.DeviceElementIds)
                    set.Add(id);
            return set;
        }

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
