using System;

namespace Pulse.Revit.Storage
{
    /// <summary>
    /// Central registry of Extensible-Storage schema identifiers and field names.
    /// NEVER change an existing GUID — create a new version entry instead.
    /// All schemas use AccessLevel.Public for both read and write.
    /// </summary>
    public static class SchemaDefinitions
    {
        // ─── Common ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Name of the DataStorage element that holds Pulse settings.
        /// </summary>
        public const string DataStorageName = "PulseSettings";

        // ─── Module settings schema ──────────────────────────────────────────────

        public static readonly Guid ModuleSettingsSchemaGuid =
            new Guid("A7E3B1C2-4D5F-6A7B-8C9D-0E1F2A3B4C5E");

        public const string SettingsJsonField  = "SettingsJson";
        public const string SchemaVersionField = "SchemaVersion";

        // ─── Diagram settings schema ─────────────────────────────────────────────

        public static readonly Guid DiagramSettingsSchemaGuid =
            new Guid("B8F4C2D3-5E6A-7B8C-9D0E-1F2A3B4C5D6E");

        public const string DiagramSettingsJsonField  = "DiagramSettingsJson";
        public const string DiagramSchemaVersionField = "DiagramSchemaVersion";

        // ─── Topology assignments schema ──────────────────────────────────────

        public static readonly Guid TopologyAssignmentsSchemaGuid =
            new Guid("C9D5E3F4-6A7B-8C9D-0E1F-2A3B4C5D6E7F");

        public const string TopologyAssignmentsJsonField    = "TopologyAssignmentsJson";
        public const string TopologyAssignmentsVersionField = "TopologyAssignmentsVersion";
        // ─── BOQ settings schema ───────────────────────────────────────────────
        // GUID must never be changed — create a new versioned entry instead.

        public static readonly Guid BoqSettingsSchemaGuid =
            new Guid("D1A6F4E5-7B8C-9D0E-1F2A-3B4C5D6E7F8A");

        public const string BoqSettingsJsonField    = "BoqSettingsJson";
        public const string BoqSettingsVersionField = "BoqSettingsVersion";    }
}
