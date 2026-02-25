using System;

namespace Pulse.Revit.Storage
{
    /// <summary>
    /// Central registry of Extensible-Storage schema identifiers, field names, and
    /// version constants.  NEVER change an existing GUID — create a new version instead.
    ///
    /// V1 schemas (legacy) use <c>AccessLevel.Public</c> for both read and write.
    /// V2 schemas (current) use <c>AccessLevel.Public</c> read / <c>AccessLevel.Vendor</c>
    /// write and include a Pulse marker field for entity ownership validation.
    /// </summary>
    public static class SchemaDefinitions
    {
        // ─── Common ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Name of the DataStorage element that holds Pulse settings.
        /// Used to locate the storage element in the document.
        /// </summary>
        public const string DataStorageName = "PulseSettings";

        /// <summary>Marker field name present on all V2 schemas for ownership validation.</summary>
        public const string PulseMarkerField = "PulseMarker";

        /// <summary>Expected marker value. Entities whose marker differs are rejected on read.</summary>
        public const string PulseMarkerValue = "Pulse";

        // ─── Module settings schema ──────────────────────────────────────────────

        /// <summary>V1 (legacy) module settings schema GUID. Used for read-back only.</summary>
        public static readonly Guid ModuleSettingsSchemaGuid =
            new Guid("A7E3B1C2-4D5F-6A7B-8C9D-0E1F2A3B4C5E");

        /// <summary>V2 (current) module settings schema GUID — vendor write-lock + marker.</summary>
        public static readonly Guid ModuleSettingsV2SchemaGuid =
            new Guid("A7E3B1C2-4D5F-6A7B-8C9D-1F2A3B4C5E6F");

        /// <summary>Current version written into module-settings entities.</summary>
        public const int ModuleSettingsSchemaVersion = 2;

        /// <summary>Field name for the JSON blob storing all module settings.</summary>
        public const string SettingsJsonField = "SettingsJson";

        /// <summary>Field name for the schema version integer.</summary>
        public const string SchemaVersionField = "SchemaVersion";

        // ─── Diagram settings schema ─────────────────────────────────────────────

        /// <summary>V1 (legacy) diagram settings schema GUID. Used for read-back only.</summary>
        public static readonly Guid DiagramSettingsSchemaGuid =
            new Guid("B8F4C2D3-5E6A-7B8C-9D0E-1F2A3B4C5D6E");

        /// <summary>V2 (current) diagram settings schema GUID — vendor write-lock + marker.</summary>
        public static readonly Guid DiagramSettingsV2SchemaGuid =
            new Guid("B8F4C2D3-5E6A-7B8C-9D0E-2F3A4B5C6D7E");

        /// <summary>Current version written into diagram-settings entities.</summary>
        public const int DiagramSettingsSchemaVersion = 2;

        /// <summary>Field name for the JSON blob storing diagram display preferences.</summary>
        public const string DiagramSettingsJsonField = "DiagramSettingsJson";

        /// <summary>Field name for the diagram schema version integer.</summary>
        public const string DiagramSchemaVersionField = "DiagramSchemaVersion";

        // ─── Topology assignments schema ──────────────────────────────────────

        /// <summary>V1 (legacy) topology assignments schema GUID. Used for read-back only.</summary>
        public static readonly Guid TopologyAssignmentsSchemaGuid =
            new Guid("C9D5E3F4-6A7B-8C9D-0E1F-2A3B4C5D6E7F");

        /// <summary>V2 (current) topology assignments schema GUID — vendor write-lock + marker.</summary>
        public static readonly Guid TopologyAssignmentsV2SchemaGuid =
            new Guid("C9D5E3F4-6A7B-8C9D-0E1F-3A4B5C6D7E8F");

        /// <summary>Current version written into topology-assignment entities.</summary>
        public const int TopologyAssignmentsSchemaVersion = 2;

        /// <summary>Field name for the JSON blob storing topology assignments.</summary>
        public const string TopologyAssignmentsJsonField = "TopologyAssignmentsJson";

        /// <summary>Field name for the topology assignments schema version integer.</summary>
        public const string TopologyAssignmentsVersionField = "TopologyAssignmentsVersion";
    }
}
