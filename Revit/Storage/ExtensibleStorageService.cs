using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;
using Pulse.Core.Boq;
using Pulse.Core.Logging;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Revit.Storage
{
    /// <summary>
    /// Manages reading and writing Pulse settings to Revit Extensible Storage.
    /// Settings are stored as JSON blobs on a named DataStorage element.
    /// All schemas use AccessLevel.Public for read and write.
    /// All writes happen inside a Revit transaction.
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
        /// Returns null if no settings have been stored yet.
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

                string json = ReadJsonFromStorage(dataStorage,
                    SchemaDefinitions.ModuleSettingsSchemaGuid,
                    SchemaDefinitions.SettingsJsonField);

                if (string.IsNullOrWhiteSpace(json)) return null;

                var settings = JsonConvert.DeserializeObject<Dictionary<string, ModuleSettings>>(json);
                _logger.Info($"Loaded settings for {settings?.Count ?? 0} module(s) from ES.");
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
        /// Must be called from within a Revit API context.
        /// </summary>
        public bool WriteSettings(Dictionary<string, ModuleSettings> settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                var schema = GetOrCreateModuleSettingsSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save Settings"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.SchemaVersionField, 1);
                    entity.Set(SchemaDefinitions.SettingsJsonField, json);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info($"Saved settings for {settings.Count} module(s) to ES.");
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
        /// Returns null if nothing has been saved yet.
        /// </summary>
        public LevelVisibilitySettings ReadDiagramSettings()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return null;

                string json = ReadJsonFromStorage(dataStorage,
                    SchemaDefinitions.DiagramSettingsSchemaGuid,
                    SchemaDefinitions.DiagramSettingsJsonField);

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
        /// Must be called from within a Revit API context.
        /// </summary>
        public bool WriteDiagramSettings(LevelVisibilitySettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.None);
                var schema = GetOrCreateDiagramSettingsSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save Diagram Settings"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.DiagramSchemaVersionField, 1);
                    entity.Set(SchemaDefinitions.DiagramSettingsJsonField, json);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved diagram settings to ES.");
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
        /// Returns a fresh empty store if nothing has been saved yet.
        /// </summary>
        public TopologyAssignmentsStore ReadTopologyAssignments()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return new TopologyAssignmentsStore();

                string json = ReadJsonFromStorage(dataStorage,
                    SchemaDefinitions.TopologyAssignmentsSchemaGuid,
                    SchemaDefinitions.TopologyAssignmentsJsonField);

                if (string.IsNullOrWhiteSpace(json))
                    return new TopologyAssignmentsStore();

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
        /// Write per-document topology assignments to Extensible Storage.
        /// Must be called from within a Revit API context.
        /// </summary>
        public bool WriteTopologyAssignments(TopologyAssignmentsStore store)
        {
            try
            {
                string json = JsonConvert.SerializeObject(store, Formatting.None);
                var schema = GetOrCreateTopologyAssignmentsSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save Topology Assignments"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.TopologyAssignmentsVersionField, 1);
                    entity.Set(SchemaDefinitions.TopologyAssignmentsJsonField, json);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved topology assignments to ES.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write topology assignments to Extensible Storage.", ex);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  BOQ settings
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read per-document BOQ settings from Extensible Storage.
        /// Returns null if nothing has been saved yet.
        /// </summary>
        public BoqSettings ReadBoqSettings()
        {
            try
            {
                var dataStorage = FindDataStorage();
                if (dataStorage == null) return null;

                string json = ReadJsonFromStorage(dataStorage,
                    SchemaDefinitions.BoqSettingsSchemaGuid,
                    SchemaDefinitions.BoqSettingsJsonField);

                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonConvert.DeserializeObject<BoqSettings>(json);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to read BOQ settings from Extensible Storage.", ex);
                return null;
            }
        }

        /// <summary>
        /// Write BOQ settings to Extensible Storage.
        /// Must be called from within a Revit API context (ExternalEvent handler).
        /// </summary>
        public bool WriteBoqSettings(BoqSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.None);
                var schema = GetOrCreateBoqSettingsSchema();

                using (var tx = new Transaction(_doc, "Pulse: Save BOQ Settings"))
                {
                    tx.Start();
                    var dataStorage = FindDataStorage() ?? CreateDataStorage();
                    var entity = new Entity(schema);
                    entity.Set(SchemaDefinitions.BoqSettingsVersionField, 1);
                    entity.Set(SchemaDefinitions.BoqSettingsJsonField, json);
                    dataStorage.SetEntity(entity);
                    tx.Commit();
                }

                _logger.Info("Saved BOQ settings to ES.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write BOQ settings to Extensible Storage.", ex);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  DataStorage helpers
        // ═══════════════════════════════════════════════════════════════════════

        private DataStorage FindDataStorage()
        {
            foreach (DataStorage ds in new FilteredElementCollector(_doc).OfClass(typeof(DataStorage)))
            {
                if (ds.Name == SchemaDefinitions.DataStorageName)
                    return ds;
            }
            return null;
        }

        private DataStorage CreateDataStorage()
        {
            var ds = DataStorage.Create(_doc);
            ds.Name = SchemaDefinitions.DataStorageName;
            _logger.Info("Created new Pulse DataStorage element.");
            return ds;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Schema builders
        // ═══════════════════════════════════════════════════════════════════════

        private static Schema GetOrCreateModuleSettingsSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.ModuleSettingsSchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.ModuleSettingsSchemaGuid);
            builder.SetSchemaName("PulseModuleSettings");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(SchemaDefinitions.SchemaVersionField, typeof(int));
            builder.AddSimpleField(SchemaDefinitions.SettingsJsonField, typeof(string));
            return builder.Finish();
        }

        private static Schema GetOrCreateDiagramSettingsSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.DiagramSettingsSchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.DiagramSettingsSchemaGuid);
            builder.SetSchemaName("PulseDiagramSettings");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(SchemaDefinitions.DiagramSchemaVersionField, typeof(int));
            builder.AddSimpleField(SchemaDefinitions.DiagramSettingsJsonField, typeof(string));
            return builder.Finish();
        }

        private static Schema GetOrCreateTopologyAssignmentsSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.TopologyAssignmentsSchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.TopologyAssignmentsSchemaGuid);
            builder.SetSchemaName("PulseTopologyAssignments");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(SchemaDefinitions.TopologyAssignmentsVersionField, typeof(int));
            builder.AddSimpleField(SchemaDefinitions.TopologyAssignmentsJsonField, typeof(string));
            return builder.Finish();
        }

        private static Schema GetOrCreateBoqSettingsSchema()
        {
            var schema = Schema.Lookup(SchemaDefinitions.BoqSettingsSchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaDefinitions.BoqSettingsSchemaGuid);
            builder.SetSchemaName("PulseBoqSettings");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(SchemaDefinitions.BoqSettingsVersionField, typeof(int));
            builder.AddSimpleField(SchemaDefinitions.BoqSettingsJsonField, typeof(string));
            return builder.Finish();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Read helper
        // ═══════════════════════════════════════════════════════════════════════

        private string ReadJsonFromStorage(DataStorage dataStorage, Guid schemaGuid, string jsonFieldName)
        {
            var schema = Schema.Lookup(schemaGuid);
            if (schema == null) return null;
            if (!dataStorage.IsValidObject) return null;

            var entity = dataStorage.GetEntity(schema);
            if (entity == null || !entity.IsValid()) return null;

            string json = entity.Get<string>(jsonFieldName);
            return string.IsNullOrWhiteSpace(json) ? null : json;
        }
    }
}
