using System.Collections.Generic;
using Pulse.Core.Rules;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// A collection of validation rules for a specific module.
    /// Evaluates all rules against the module data and collects results.
    /// </summary>
    public interface IRulePack
    {
        /// <summary>All rules in this pack.</summary>
        IReadOnlyList<IRule> Rules { get; }

        /// <summary>
        /// Run all rules against the module data.
        /// Results are appended to ModuleData.RuleResults.
        /// </summary>
        void Evaluate(ModuleData data);
    }
}
