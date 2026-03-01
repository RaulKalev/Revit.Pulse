using System;
using System.IO;
using Newtonsoft.Json;
using Pulse.Core.Boq;
using Pulse.Core.Logging;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Application-layer service for BOQ settings.
    /// Provides:
    ///   • Load from / save to Extensible Storage (via <see cref="StorageFacade"/>)
    ///   • Export to user-specified JSON file
    ///   • Import from user-specified JSON file with basic validation
    ///
    /// Extensible-Storage writes are always async (ExternalEvent). Reads must be
    /// performed on the Revit API thread before the BOQ window opens.
    /// </summary>
    public class BoqSettingsService
    {
        private readonly StorageFacade _storageFacade;
        private readonly ILogger _logger;

        public BoqSettingsService(StorageFacade storageFacade, ILogger logger = null)
        {
            _storageFacade = storageFacade ?? throw new ArgumentNullException(nameof(storageFacade));
            _logger = logger ?? new DebugLogger("Pulse.BOQ");
        }

        // ── Extensible Storage ────────────────────────────────────────────────

        /// <summary>
        /// Read BOQ settings for the given module scope from Extensible Storage.
        /// Must be called on the Revit API thread. Returns null when no settings exist.
        /// </summary>
        public BoqSettings Load(Autodesk.Revit.DB.Document doc, string moduleKey)
        {
            var settings = _storageFacade.ReadBoqSettings(doc);
            if (settings != null && !string.Equals(settings.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase))
            {
                // Different module scope — ignore and return null so caller creates defaults.
                _logger.Info($"BOQ settings module key mismatch: stored='{settings.ModuleKey}', requested='{moduleKey}'. Ignoring.");
                return null;
            }
            return settings;
        }

        /// <summary>
        /// Persist BOQ settings to Extensible Storage via ExternalEvent.
        /// Safe to call from the WPF UI thread.
        /// </summary>
        public void Save(BoqSettings settings, Action onSaved = null, Action<Exception> onError = null)
        {
            if (settings == null) return;
            _storageFacade.SaveBoqSettings(settings, onSaved, onError);
        }

        // ── JSON Export / Import ──────────────────────────────────────────────

        /// <summary>
        /// Export <paramref name="settings"/> to a JSON file at <paramref name="filePath"/>.
        /// Overwrites existing file.
        /// </summary>
        public void ExportToJson(BoqSettings settings, string filePath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path must not be empty.", nameof(filePath));

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            _logger.Info($"Exported BOQ settings to '{filePath}'.");
        }

        /// <summary>
        /// Import BOQ settings from a JSON file.
        /// Validates that the file is readable and the JSON deserialises correctly.
        /// Unknown JSON fields are silently ignored (forward-compatibility).
        /// Returns null and logs an error if the file is missing or corrupt.
        /// </summary>
        public BoqSettings ImportFromJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.Error($"BOQ import: file not found '{filePath}'.");
                    return null;
                }

                string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var settings = JsonConvert.DeserializeObject<BoqSettings>(json,
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore });

                if (settings == null)
                {
                    _logger.Error($"BOQ import: deserialization returned null for '{filePath}'.");
                    return null;
                }

                _logger.Info($"Imported BOQ settings v{settings.SettingsVersion} from '{filePath}'.");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.Error($"BOQ import failed for '{filePath}'.", ex);
                return null;
            }
        }
    }
}
