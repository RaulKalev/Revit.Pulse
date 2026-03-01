namespace Pulse.Core.Boq
{
    /// <summary>
    /// One level in a multi-level grouping specification.
    /// Groups are applied in order of their <see cref="Priority"/> (lower = outer group).
    /// </summary>
    public class BoqGroupingRule
    {
        /// <summary>
        /// Field key to group by.  Must be a valid key on <see cref="BoqItem"/>
        /// (standard field, discovered parameter, or custom column).
        /// </summary>
        public string FieldKey { get; set; } = string.Empty;

        /// <summary>User-visible label shown in the settings UI.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Grouping depth / nesting order.
        /// 0 = outermost group, 1 = next level, etc.
        /// </summary>
        public int Priority { get; set; }
    }
}
