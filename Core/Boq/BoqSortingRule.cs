namespace Pulse.Core.Boq
{
    /// <summary>
    /// One level in a multi-level sort specification.
    /// Rules are applied in order of their <see cref="Priority"/> (lower = primary sort).
    /// </summary>
    public class BoqSortingRule
    {
        /// <summary>Field key to sort by.</summary>
        public string FieldKey { get; set; } = string.Empty;

        /// <summary>User-visible label for the settings UI.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Sort direction.</summary>
        public BoqSortDirection Direction { get; set; } = BoqSortDirection.Ascending;

        /// <summary>
        /// WPF-friendly bool helper — maps to/from <see cref="Direction"/>.
        /// Used by the CheckBox in the settings panel ("Descending?").
        /// </summary>
        public bool IsDescending
        {
            get => Direction == BoqSortDirection.Descending;
            set => Direction = value ? BoqSortDirection.Descending : BoqSortDirection.Ascending;
        }

        /// <summary>
        /// Sort depth / order.
        /// 0 = primary sort key, 1 = secondary, etc.
        /// </summary>
        public int Priority { get; set; }
    }

    public enum BoqSortDirection
    {
        Ascending,
        Descending
    }
}
