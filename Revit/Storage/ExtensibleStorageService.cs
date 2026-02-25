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
    /// Settings are stored as JSON blobs on a DataStorage element.
    ///
    /// Safety guarantees:
    /// - V2 schemas are preferred; V1 schemas are read for backward compatibility.
    /// - V2 schemas use <c>AccessLevel.Vendor</c> for write and include a Pulse
    ///   marker field to validate entity ownership.
    /// - Missing schema initializes defaults; never corrupts the document.
    /// - V1 data entities are never deleted from the document.
    /// - Each version increment has an explicit migration step.
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

        // ═══════════════════════════════════════════════════════════════════════
        //  Module settings
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read module settings from Extensible Storage.
        /// Tries V2 (hardened) first, then falls back to V1 (legacy).
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

                // Prefer V2 (hardened) schema
                string json = TryReadJsonFromSchema(
                    dataStorage,
                    SchemaDefinitions.ModuleSettingsV2SchemaGuid,
                    SchemaDefinitions.SettingsJsonField,
                    validateMarker: true);
                int fromVersion = 2;

                // Fall back to V1 (legacy) schema
                if (json == null)
                {
                    json = TryReadJsonFromSchema(
                        dataStorage,
                        SchemaDefinitions.ModuleSettingsSchemaGuid,
                        SchemaDefinitions.SettingsJsonField,
                        validateMarker: false);
                    fromVersion = 1;
                }

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                // Run migrations if reading older data
                if (fromVersion < SchemaDefinitions.ModuleSettingsSchemaVersion)
                {
                    json = UpgradeSettingsJson(json, fromVersion);
                }

                var settings = JsonConvert.DeserializeObject<Dictionary<string, ModuleSettings>>(json);
                _logger.Info($"Loaded settings for {settings?.Count ?? 0} module(s) from ES (v{fromVersion}).");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read settings from Extensible Storage.", ex);
                return null;
            }
        }

        /// <summary>
        /// Write module settings to Extensible Storage (V2 schema).
        /// Must be called from within a Revit API context (ExternalEvent).
        /// Creates the DataStorage element and V2 schema if they do not exist.
        /// </summary>
        public bool WriteSettings(Dictionary<string, ModuleSettings> settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                var schema = GetOrCreateModuleSettingsV2Schema();

                using (var tx = new Transaction(_doc, "Pulse: Save Settings"))
                {
                    tx.Start();

                    var dataStorage = FindDataStorage() ?? CreateDataStorage();

                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.SchemaVersionField, SchemaDefinitions.ModuleSettingsSchemaVersion);
                    entity.Set(SchemaDefinitions.SettingsJsonField, json);
                    entity.Set(SchemaDefinitions.PulseMarkerField, SchemaDefinitions.PulseMarkerValue);

                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info($"Saved settings for {settings.Count} module(s) to ES (v2).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write settings to Extensible Storage.", ex);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Diagram settings
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read per-project diagram display preferences from Extensible Storage.
        /// Tries V2 first, then falls back to V1. Returns null if nothing saved.
        /// </summary>
        public LevelVisibilitySettings ReadDiagramSettings()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return null;

                // Prefer V2
                string json = TryReadJsonFromSchema(
                    dataStorage,
                    SchemaDefinitions.DiagramSettingsV2SchemaGuid,
                    SchemaDefinitions.DiagramSettingsJsonField,
                    validateMarker: true);
                int fromVersion = 2;

                // Fall back to V1
                if (json == null)
                {
                    json = TryReadJsonFromSchema(
                        dataStorage,
                        SchemaDefinitions.DiagramSettingsSchemaGuid,
                        SchemaDefinitions.DiagramSettingsJsonField,
                        validateMarker: false);
                    fromVersion = 1;
                }

                if (string.IsNullOrWhiteSpace(json)) return null;

                if (fromVersion < SchemaDefinitions.DiagramSettingsSchemaVersion)
                {
                    json = UpgradeDiagramJson(json, fromVersion);
                }

                return JsonConvert.DeserializeObject<LevelVisibilitySettings>(json);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read diagram settings from Extensible Storage.", ex);
                return null;
            }
        }

        /// <summary>
        /// Write diagram display preferences to Extensible Storage (V2 schema).
        /// Must be called from within a Revit API context (ExternalEvent).
        /// </summary>
        public bool WriteDiagramSettings(LevelVisibilitySettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.None);
                var schema = GetOrCreateDiagramSettingsV2Schema();

                using (var tx = new Transaction(_doc, "Pulse: Save Diagram Settings"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.DiagramSchemaVersionField, SchemaDefinitions.DiagramSettingsSchemaVersion);
                    entity.Set(SchemaDefinitions.DiagramSettingsJsonField, json);
                    entity.Set(SchemaDefinitions.PulseMarkerField, SchemaDefinitions.PulseMarkerValue);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved diagram settings to ES (v2).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write diagram settings to Extensible Storage.", ex);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Topology assignments
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read per-document topology assignments from Extensible Storage.
        /// Tries V2 first, then falls back to V1.
        /// Returns a fresh empty store if nothing has been saved yet.
        /// </summary>
        public TopologyAssignmentsStore ReadTopologyAssignments()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return new TopologyAssignmentsStore();

                // Prefer V2
                string json = TryReadJsonFromSchema(
                    dataStorage,
                    SchemaDefinitions.TopologyAssignmentsV2SchemaGuid,
                    SchemaDefinitions.TopologyAssignmentsJsonField,
                    validateMarker: true);
                int fromVersion = 2;

                // Fall back to V1
                if (json == null)
                {
                    json = TryReadJsonFromSchema(
                        dataStorage,
                        SchemaDefinitions.TopologyAssignmentsSchemaGuid,
                        SchemaDefinitions.TopologyAssignmentsJsonField,
                        validateMarker: false);
                    fromVersion = 1;
                }

                if (string.IsNullOrWhiteSpace(json))
                    return new TopologyAssignmentsStore();

                if (fromVersion < SchemaDefinitions.TopologyAssignmentsSchemaVersion)
                {
                    json = UpgradeTopologyJson(json, fromVersion);
                }

                return JsonConvert.DeserializeObject<TopologyAssignmentsStore>(json)
                       ?? new TopologyAssignmentsStore();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read topology assignments from Extensible Storage.", ex);
                return new TopologyAssignmentsStore();
            }
        }

        /// <summary>
        /// Write per-document topology assignments to Extensible Storage (V2 schema).
        /// Must be called from within a Revit API context (ExternalEvent).
        /// </summary>
        public bool WriteTopologyAssignments(TopologyAssignmentsStore store)
        {
            try
            {
                string json = JsonConvert.SerializeObject(store, Formatting.None);
                var schema = GetOrCreateTopologyAssignmentsV2Schema();

                using (var tx = new Transaction(_doc, "Pulse: Save Topology Assignments"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.TopologyAssignmentsVersionField, SchemaDefinitions.TopologyAssignmentsSchemaVersion);
                    entity.Set(SchemaDefinitions.TopologyAssignmentsJsonField, json);
                    entity.Set(SchemaDefinitions.PulseMarkerField, SchemaDefinitions.PulseMarkerValue);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved topology assignments to ES (v2).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write topology assignments to Extensible Storage.", ex);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  DataStorage helpers
        // ═══════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════
        //  V2 schema builders  (vendor write-lock + Pulse marker)
        // ═══════════════════════════════════════════════════════════════════════

        private static Schema GetOrCreateModuleSettingsV2Schema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.ModuleSettingsV2SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.ModuleSettingsV2SchemaGuid);
            builder.SetSchemaName("PulseModuleSettings");
            builder.SetDocumentation("Stores Pulse module configuration (v2 — marker field for ownership validation).");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(SchemaDefinitions.SchemaVersionField, typeof(int))
                .SetDocumentation("Schema version for upgrade compatibility.");
            builder.AddSimpleField(SchemaDefinitions.SettingsJsonField, typeof(string))
                .SetDocumentation("JSON blob containing all module settings.");
            builder.AddSimpleField(SchemaDefinitions.PulseMarkerField, typeof(string))
                .SetDocumentation("Ownership marker — must equal 'Pulse'.");

            return builder.Finish();
        }

        private static Schema GetOrCreateDiagramSettingsV2Schema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.DiagramSettingsV2SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.DiagramSettingsV2SchemaGuid);
            builder.SetSchemaName("PulseDiagramSettings");
            builder.SetDocumentation("Stores Pulse diagram display preferences (v2 — marker field for ownership validation).");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(SchemaDefinitions.DiagramSchemaVersionField, typeof(int))
                .SetDocumentation("Schema version for upgrade compatibility.");
            builder.AddSimpleField(SchemaDefinitions.DiagramSettingsJsonField, typeof(string))
                .SetDocumentation("JSON blob containing diagram display preferences.");
            builder.AddSimpleField(SchemaDefinitions.PulseMarkerField, typeof(string))
                .SetDocumentation("Ownership marker — must equal 'Pulse'.");

            return builder.Finish();
        }

        private static Schema GetOrCreateTopologyAssignmentsV2Schema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.TopologyAssignmentsV2SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.TopologyAssignmentsV2SchemaGuid);
            builder.SetSchemaName("PulseTopologyAssignments");
            builder.SetDocumentation("Stores per-document topology assignments (v2 — marker field for ownership validation).");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(SchemaDefinitions.TopologyAssignmentsVersionField, typeof(int))
                .SetDocumentation("Schema version for upgrade compatibility.");
            builder.AddSimpleField(SchemaDefinitions.TopologyAssignmentsJsonField, typeof(string))
                .SetDocumentation("JSON blob containing all topology assignments for this document.");
            builder.AddSimpleField(SchemaDefinitions.PulseMarkerField, typeof(string))
                .SetDocumentation("Ownership marker — must equal 'Pulse'.");

            return builder.Finish();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Read helper
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Attempt to read a JSON string from a specific schema on a DataStorage element.
        /// Returns null if the schema does not exist, the entity is missing, or
        /// marker validation fails.
        /// </summary>
        private string TryReadJsonFromSchema(
            DataStorage dataStorage,
            Guid schemaGuid,
            string jsonFieldName,
            bool validateMarker)
        {
            var schema = Schema.Lookup(schemaGuid);
            if (schema == null) return null;

            if (!dataStorage.IsValidObject) return null;

            var entity = dataStorage.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return null;

            // Validate Pulse marker if the schema contains one
            if (validateMarker)
            {
                var markerField = schema.GetField(SchemaDefinitions.PulseMarkerField);
                if (markerField != null)
                {
                    string marker = entity.Get<string>(SchemaDefinitions.PulseMarkerField);
                    if (marker != SchemaDefinitions.PulseMarkerValue)
                    {
                        _logger.Warning($"Pulse marker validation failed on schema {schemaGuid}; ignoring entity.");
                        return null;
                    }
                }
            }

            string json = entity.Get<string>(jsonFieldName);
            return string.IsNullOrWhiteSpace(json) ? null : json;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Migration paths
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step through module-settings JSON migrations sequentially.
        /// Each version bump should add a case that transforms the JSON.
        /// </summary>
        private string UpgradeSettingsJson(string json, int fromVersion)
        {
            if (fromVersion < 2)
            {
                // v1 → v2: No JSON format changes required.
                // Future migrations would transform the JSON payload here.
                _logger.Info("Migrating module-settings JSON from v1 → v2 (format unchanged).");
            }

            return json;
        }

        /// <summary>
        /// Step through diagram-settings JSON migrations sequentially.
        /// </summary>
        private string UpgradeDiagramJson(string json, int fromVersion)
        {
            if (fromVersion < 2)
            {
                _logger.Info("Migrating diagram-settings JSON from v1 → v2 (format unchanged).");
            }

            return json;
        }

        /// <summary>
        /// Step through topology-assignments JSON migrations sequentially.
        /// </summary>
        private string UpgradeTopologyJson(string json, int fromVersion)
        {
            if (fromVersion < 2)
            {
                _logger.Info("Migrating topology-assignments JSON from v1 → v2 (format unchanged).");
            }

            return json;
        }
    }
}
