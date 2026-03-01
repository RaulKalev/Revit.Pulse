using System.Collections.Generic;

namespace Pulse.Core.Boq
{
    /// <summary>
    /// Full persisted settings for one BOQ view.
    ///
    /// Serialised to JSON and stored in:
    ///   • Revit Extensible Storage (per-document, per <see cref="ModuleKey"/>)
    ///   • (optionally) exported user JSON files for transfer between projects
    ///
    /// Version history is tracked via <see cref="SettingsVersion"/> so that future
    /// migrations can be applied on load without breaking older projects.
    /// </summary>
    public class BoqSettings
    {
        // ── Identity / versioning ────────────────────────────────────────────

        /// <summary>
        /// Schema version string — increment when the shape of this class changes.
        /// Current: "1.0"
        /// </summary>
        public string SettingsVersion { get; set; } = "1.0";

        /// <summary>
        /// Identifies which module this BOQ belongs to (e.g. "FireAlarm").
        /// Used as the storage scope key so multiple modules can coexist.
        /// </summary>
        public string ModuleKey { get; set; } = "FireAlarm";

        // ── Column configuration ─────────────────────────────────────────────

        /// <summary>All column definitions including visibility and display-order.</summary>
        public List<BoqColumnDefinition> VisibleColumns { get; set; } = new List<BoqColumnDefinition>();

        /// <summary>User-defined computed columns (Concat / Sum / JoinDelimited).</summary>
        public List<BoqCustomColumnDefinition> CustomColumns { get; set; } = new List<BoqCustomColumnDefinition>();

        /// <summary>
        /// Explicit ordered list of <see cref="BoqColumnDefinition.FieldKey"/> values
        /// that determines DataGrid column order.  Columns not listed appear after those that are.
        /// </summary>
        public List<string> ColumnOrder { get; set; } = new List<string>();

        // ── View rules ────────────────────────────────────────────────────────

        /// <summary>Active grouping rules, ordered from outermost to innermost group.</summary>
        public List<BoqGroupingRule> GroupingRules { get; set; } = new List<BoqGroupingRule>();

        /// <summary>Active sorting rules, ordered from primary to secondary sort.</summary>
        public List<BoqSortingRule> SortingRules { get; set; } = new List<BoqSortingRule>();
    }
}
