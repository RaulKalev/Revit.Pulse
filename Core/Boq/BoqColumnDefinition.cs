namespace Pulse.Core.Boq
{
    /// <summary>
    /// Describes one column in the BOQ DataGrid.
    ///
    /// Standard columns use the well-known FieldKey values ("Category", "FamilyName",
    /// "TypeName", "Level", "Panel", "Loop").  Discovered parameters use the raw
    /// Revit parameter name as the FieldKey.  Custom computed columns are identified
    /// by their user-defined <see cref="FieldKey"/> (must be unique).
    /// </summary>
    public class BoqColumnDefinition
    {
        /// <summary>
        /// Unique key used to look up the value on a <see cref="BoqItem"/>.
        /// For standard fields this equals the property name; for parameters it equals
        /// the Revit parameter name; for custom columns it equals the user-supplied name.
        /// </summary>
        public string FieldKey { get; set; } = string.Empty;

        /// <summary>User-visible column header text.</summary>
        public string Header { get; set; } = string.Empty;

        /// <summary>Whether the column is visible in the DataGrid.</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>Display order (0 = leftmost). -1 means unset.</summary>
        public int DisplayOrder { get; set; } = -1;

        /// <summary>True if this column was discovered from element parameters (not a fixed standard field).</summary>
        public bool IsDiscovered { get; set; }

        /// <summary>True if this is a computed custom column defined by a <see cref="BoqCustomColumnDefinition"/>.</summary>
        public bool IsCustom { get; set; }

        public BoqColumnDefinition() { }

        public BoqColumnDefinition(string fieldKey, string header, bool isVisible = true)
        {
            FieldKey    = fieldKey;
            Header      = header;
            IsVisible   = isVisible;
        }
    }
}
