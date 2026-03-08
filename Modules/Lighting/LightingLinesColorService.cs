using System.Collections.Generic;
using Newtonsoft.Json;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Manages per-line color data for the Lighting module.
    /// Stored as an opaque JSON blob in
    /// <see cref="TopologyAssignmentsStore.ModuleBlobs"/>["Lighting.Lines"]
    /// using the key format <c>"controllerName::lineName"</c>.
    /// </summary>
    public sealed class LightingLinesColorService
    {
        public const string BlobKey = "Lighting.Lines";

        private readonly TopologyAssignmentsService _assignmentsService;
        private Dictionary<string, LightingLineData> _cache;

        public LightingLinesColorService(TopologyAssignmentsService assignmentsService)
        {
            _assignmentsService = assignmentsService;
        }

        /// <summary>Invalidates the cache. Call after TopologyAssignmentsService.Load().</summary>
        public void OnStoreLoaded()
        {
            _cache = null;
        }

        /// <summary>Returns the stored hex color for a given controller+line, or the default magenta.</summary>
        public string GetColor(string controllerName, string lineName)
        {
            var cache = EnsureCache();
            string key = MakeKey(controllerName, lineName);
            return cache.TryGetValue(key, out var data) ? data.ColorHex : LightingLineData.DefaultColor;
        }

        /// <summary>Updates the stored hex color for a given controller+line.</summary>
        public void SetColor(string controllerName, string lineName, string colorHex)
        {
            var cache = EnsureCache();
            string key = MakeKey(controllerName, lineName);
            if (!cache.TryGetValue(key, out var data))
            {
                data = new LightingLineData();
                cache[key] = data;
            }
            data.ColorHex = colorHex ?? LightingLineData.DefaultColor;
        }

        /// <summary>Serialises the cache back to the store blob. Call before RequestSave.</summary>
        public void PersistToStore()
        {
            if (_cache == null) return;
            _assignmentsService.Store.ModuleBlobs[BlobKey] = JsonConvert.SerializeObject(_cache);
        }

        private static string MakeKey(string ctrl, string line) =>
            $"{ctrl ?? string.Empty}::{line ?? string.Empty}";

        private Dictionary<string, LightingLineData> EnsureCache()
        {
            if (_cache != null) return _cache;

            _assignmentsService.Store.ModuleBlobs.TryGetValue(BlobKey, out string json);
            if (!string.IsNullOrEmpty(json))
            {
                try { _cache = JsonConvert.DeserializeObject<Dictionary<string, LightingLineData>>(json); }
                catch { /* corrupt blob — start fresh */ }
            }
            if (_cache == null) _cache = new Dictionary<string, LightingLineData>();
            return _cache;
        }
    }
}
