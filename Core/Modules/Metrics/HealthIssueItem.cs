using System.Collections.Generic;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Overall health status rolled up across one or more rule categories.
    /// </summary>
    public enum HealthStatus
    {
        Ok,      // no violations
        Warning, // warning-level violations
        Error    // error-level violations
    }

    /// <summary>
    /// A single row in the Health Status section.
    /// Maps to one rule category (e.g. "Duplicate addresses") and aggregates
    /// all matching violations.
    /// </summary>
    public sealed class HealthIssueItem
    {
        /// <summary>Internal rule name used to filter relevant RuleResults.</summary>
        public string RuleName { get; set; }

        /// <summary>User-visible description shown in the health row.</summary>
        public string Description { get; set; }

        /// <summary>Number of violations (0 means this rule is clean).</summary>
        public int Count { get; set; }

        /// <summary>Worst severity found for this category.</summary>
        public HealthStatus Status { get; set; }

        /// <summary>
        /// Revit ElementIds of all affected elements — used to drive Revit highlight.
        /// </summary>
        public IReadOnlyList<long> AffectedElementIds { get; set; }
            = System.Array.Empty<long>();
    }
}
