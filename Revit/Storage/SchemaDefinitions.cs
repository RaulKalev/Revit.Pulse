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

        // ─── Diagram settings schema ─────────────────────────────────────────────

        /// <summary>
        /// Schema for storing diagram display preferences (level line visibility).
        /// Stored as a second entity on the same DataStorage element.
        /// NEVER change this GUID — create a new version instead.
        /// </summary>
        public static readonly Guid DiagramSettingsSchemaGuid = new Guid("B8F4C2D3-5E6A-7B8C-9D0E-1F2A3B4C5D6E");

        /// <summary>Current version of the Diagram settings schema.</summary>
        public const int DiagramSettingsSchemaVersion = 1;

        /// <summary>Field name for the JSON blob storing diagram display preferences.</summary>
        public const string DiagramSettingsJsonField = "DiagramSettingsJson";

        /// <summary>Field name for the diagram schema version integer.</summary>
        public const string DiagramSchemaVersionField = "DiagramSchemaVersion";
    }
}
