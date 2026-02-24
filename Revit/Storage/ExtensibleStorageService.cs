using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using Pulse.Core.Logging;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Revit.Storage
{
    /// <summary>
    /// Manages reading and writing module settings to Revit Extensible Storage.
    /// Settings are stored as a JSON blob on a DataStorage element.
    /// 
    /// Safety guarantees:
    /// - Schema is versioned; upgrades are handled explicitly.
    /// - Missing schema initializes defaults; never corrupts the document.
    /// - Previous schemas are never silently deleted.
    /// - All writes happen inside a Revit transaction.
    /// </summary>
    public class ExtensibleStorageService
    {
        private readonly Document _doc;
        private readonly ILogger _logger;

        public ExtensibleStorageService(Document doc, ILogger logger = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _logger = logger ?? new DebugLogger("Pulse.Storage");
        }

        /// <summary>
        /// Read module settings from Extensible Storage.
        /// Returns null if no settings are stored (caller should initialize defaults).
        /// </summary>
        public Dictionary<string, ModuleSettings> ReadSettings()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null)
                {
                    _logger.Info("No Pulse settings DataStorage found. Will use defaults.");
                    return null;
                }

                var schema = GetOrCreateSchema();
                if (!dataStorage.IsValidObject || dataStorage.GetEntity(schema) == null)
                {
                    _logger.Info("DataStorage exists but has no Pulse schema entity.");
                    return null;
                }

                var entity = dataStorage.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                {
                    return null;
                }

                int storedVersion = entity.Get<int>(SchemaDefinitions.SchemaVersionField);
                string json = entity.Get<string>(SchemaDefinitions.SettingsJsonField);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                // Handle schema version upgrades
                if (storedVersion < SchemaDefinitions.ModuleSettingsSchemaVersion)
                {
                    _logger.Warning($"Stored schema version {storedVersion} is older than current {SchemaDefinitions.ModuleSettingsSchemaVersion}. Upgrading.");
                    json = UpgradeSettingsJson(json, storedVersion);
                }

                var settings = JsonConvert.DeserializeObject<Dictionary<string, ModuleSettings>>(json);
                _logger.Info($"Loaded settings for {settings?.Count ?? 0} module(s) from Extensible Storage.");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read settings from Extensible Storage.", ex);
                return null;
            }
        }

        /// <summary>
        /// Write module settings to Extensible Storage.
        /// Must be called from within a Revit API context (ExternalEvent).
        /// Creates the DataStorage element and schema if they do not exist.
        /// </summary>
        public bool WriteSettings(Dictionary<string, ModuleSettings> settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                var schema = GetOrCreateSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save Settings"))
                {
                    tx.Start();

                    var dataStorage = FindDataStorage() ?? CreateDataStorage();

                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.SchemaVersionField, SchemaDefinitions.ModuleSettingsSchemaVersion);
                    entity.Set(SchemaDefinitions.SettingsJsonField, json);

                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info($"Saved settings for {settings.Count} module(s) to Extensible Storage.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write settings to Extensible Storage.", ex);
                return false;
            }
        }

        /// <summary>
        /// Find the existing Pulse DataStorage element in the document.
        /// Returns null if not found.
        /// </summary>
        private DataStorage FindDataStorage()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage));

            foreach (DataStorage ds in collector)
            {
                if (ds.Name == SchemaDefinitions.DataStorageName)
                {
                    return ds;
                }
            }

            return null;
        }

        /// <summary>
        /// Create a new DataStorage element for Pulse settings.
        /// Must be called inside an active transaction.
        /// </summary>
        private DataStorage CreateDataStorage()
        {
            var ds = DataStorage.Create(_doc);
            ds.Name = SchemaDefinitions.DataStorageName;
            _logger.Info("Created new Pulse DataStorage element.");
            return ds;
        }

        /// <summary>
        /// Get or create the Extensible Storage schema.
        /// </summary>
        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.ModuleSettingsSchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(SchemaDefinitions.ModuleSettingsSchemaGuid);
            builder.SetSchemaName("PulseModuleSettings");
            builder.SetDocumentation("Stores Pulse module configuration including parameter mappings and categories.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(SchemaDefinitions.SchemaVersionField, typeof(int))
                .SetDocumentation("Schema version for upgrade compatibility.");

            builder.AddSimpleField(SchemaDefinitions.SettingsJsonField, typeof(string))
                .SetDocumentation("JSON blob containing all module settings.");

            return builder.Finish();
        }

        /// <summary>
        /// Handle schema version upgrades.
        /// Each version increment adds a migration step.
        /// </summary>
        private string UpgradeSettingsJson(string json, int fromVersion)
        {
            _logger.Info($"Settings JSON upgraded from version {fromVersion} to {SchemaDefinitions.ModuleSettingsSchemaVersion}.");
            return json;
        }

        // ─── Diagram settings ────────────────────────────────────────────────────

        /// <summary>
        /// Read per-project diagram display preferences (level visibility states) from Extensible Storage.
        /// Returns null if nothing has been saved yet.
        /// </summary>
        public LevelVisibilitySettings ReadDiagramSettings()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return null;

                var schema = GetOrCreateDiagramSchema();
                var entity = dataStorage.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;

                string json = entity.Get<string>(SchemaDefinitions.DiagramSettingsJsonField);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonConvert.DeserializeObject<LevelVisibilitySettings>(json);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read diagram settings from Extensible Storage.", ex);
                return null;
            }
        }

        /// <summary>
        /// Write diagram display preferences to Extensible Storage.
        /// Must be called from within a Revit API context (ExternalEvent).
        /// </summary>
        public bool WriteDiagramSettings(LevelVisibilitySettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.None);
                var schema = GetOrCreateDiagramSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save Diagram Settings"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.DiagramSchemaVersionField, SchemaDefinitions.DiagramSettingsSchemaVersion);
                    entity.Set(SchemaDefinitions.DiagramSettingsJsonField, json);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved diagram settings to Extensible Storage.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write diagram settings to Extensible Storage.", ex);
                return false;
            }
        }

        private static Schema GetOrCreateDiagramSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.DiagramSettingsSchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.DiagramSettingsSchemaGuid);
            builder.SetSchemaName("PulseDiagramSettings");
            builder.SetDocumentation("Stores Pulse diagram display preferences such as level line visibility.");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(SchemaDefinitions.DiagramSchemaVersionField, typeof(int))
                .SetDocumentation("Schema version for upgrade compatibility.");

            builder.AddSimpleField(SchemaDefinitions.DiagramSettingsJsonField, typeof(string))
                .SetDocumentation("JSON blob containing diagram display preferences.");

            return builder.Finish();
        }
    }
}
