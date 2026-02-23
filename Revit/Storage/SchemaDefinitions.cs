using System;

namespace Pulse.Revit.Storage
{
    /// <summary>
    /// Centralized schema definitions for Extensible Storage.
    /// Each schema has a unique GUID and version number.
    /// NEVER change an existing GUID — create a new schema version instead.
    /// </summary>
    public static class SchemaDefinitions
    {
        /// <summary>
        /// Schema for storing module configuration (categories, parameter mappings).
        /// Stored on a DataStorage element in the Revit document.
        /// </summary>
        public static readonly Guid ModuleSettingsSchemaGuid = new Guid("A7E3B1C2-4D5F-6A7B-8C9D-0E1F2A3B4C5E");

        /// <summary>Current version of the ModuleSettings schema.</summary>
        public const int ModuleSettingsSchemaVersion = 1;

        /// <summary>Field name for the JSON blob storing all module settings.</summary>
        public const string SettingsJsonField = "SettingsJson";

        /// <summary>Field name for the schema version integer.</summary>
        public const string SchemaVersionField = "SchemaVersion";

        /// <summary>
        /// Name of the DataStorage element that holds Pulse settings.
        /// Used to locate the storage element in the document.
        /// </summary>
        public const string DataStorageName = "PulseSettings";
    }
}
