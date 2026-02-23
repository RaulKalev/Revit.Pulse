using System;

namespace Pulse.Core.Rules
{
    /// <summary>
    /// Represents the result of a single rule evaluation against an entity.
    /// </summary>
    public class RuleResult
    {
        /// <summary>Name of the rule that produced this result.</summary>
        public string RuleName { get; }

        /// <summary>Severity of the finding.</summary>
        public Severity Severity { get; }

        /// <summary>Human-readable description of the finding.</summary>
        public string Message { get; }

        /// <summary>
        /// Optional Revit ElementId of the affected element.
        /// Stored as long to avoid coupling Core to RevitAPI types.
        /// </summary>
        public long? ElementId { get; }

        /// <summary>Optional entity identifier within the module topology.</summary>
        public string EntityId { get; }

        public RuleResult(string ruleName, Severity severity, string message, long? elementId = null, string entityId = null)
        {
            RuleName = ruleName ?? throw new ArgumentNullException(nameof(ruleName));
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            ElementId = elementId;
            EntityId = entityId;
        }
    }
}
