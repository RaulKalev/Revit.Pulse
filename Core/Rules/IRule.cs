using Pulse.Core.Modules;

namespace Pulse.Core.Rules
{
    /// <summary>
    /// Contract for a single validation rule.
    /// Rules are stateless evaluators that inspect module data and produce results.
    /// </summary>
    public interface IRule
    {
        /// <summary>Unique name of this rule.</summary>
        string Name { get; }

        /// <summary>Human-readable description of what this rule checks.</summary>
        string Description { get; }

        /// <summary>Default severity when this rule is violated.</summary>
        Severity DefaultSeverity { get; }

        /// <summary>
        /// Evaluate the rule against collected module data.
        /// Returns zero or more results — empty means the rule passed.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<RuleResult> Evaluate(ModuleData data);
    }
}
