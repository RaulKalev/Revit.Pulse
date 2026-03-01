using System.Collections.Generic;

namespace Pulse.Core.Boq
{
    /// <summary>
    /// Defines how a custom computed column is built at runtime.
    /// No scripting engine is used — the evaluator is a deterministic
    /// structural interpreter that handles the supported <see cref="CustomColumnKind"/> values.
    /// </summary>
    public class BoqCustomColumnDefinition
    {
        /// <summary>
        /// Unique identifier for this custom column.
        /// Must match a <see cref="BoqColumnDefinition.FieldKey"/> that has <c>IsCustom = true</c>.
        /// </summary>
        public string ColumnKey { get; set; } = string.Empty;

        /// <summary>User-visible column header label.</summary>
        public string Header { get; set; } = string.Empty;

        /// <summary>The kind of computation to perform.</summary>
        public CustomColumnKind Kind { get; set; } = CustomColumnKind.Concat;

        /// <summary>
        /// Source field keys (standard field names or Revit parameter names) whose
        /// values are combined to produce the custom column value.
        /// </summary>
        public List<string> SourceKeys { get; set; } = new List<string>();

        /// <summary>
        /// Delimiter inserted between values when <see cref="Kind"/> is
        /// <see cref="CustomColumnKind.Concat"/> or <see cref="CustomColumnKind.JoinDelimited"/>.
        /// Ignored for <see cref="CustomColumnKind.Sum"/>.
        /// Default is a single space.
        /// </summary>
        public string Delimiter { get; set; } = " ";
    }

    /// <summary>
    /// The supported kinds of custom column computation.
    /// </summary>
    public enum CustomColumnKind
    {
        /// <summary>
        /// Concatenate string values of the source fields using <see cref="BoqCustomColumnDefinition.Delimiter"/>.
        /// Example: FamilyName + " " + TypeName → "Detector Smoke 57°"
        /// </summary>
        Concat,

        /// <summary>
        /// Sum numeric values of the source fields.
        /// Missing or non-numeric values are treated as 0.
        /// Example: Param("mA_Normal") + Param("mA_Alarm") → 5.5
        /// </summary>
        Sum,

        /// <summary>
        /// Join non-empty string values with the configured delimiter.
        /// Semantically the same as <see cref="Concat"/> but skips null / empty parts.
        /// </summary>
        JoinDelimited
    }
}
