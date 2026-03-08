using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Rule pack containing all Lighting validation rules.
    /// Evaluates each rule against the module data and appends results.
    /// </summary>
    public class LightingRulePack : IRulePack
    {
        private readonly List<IRule> _rules;

        public IReadOnlyList<IRule> Rules => _rules;

        public LightingRulePack()
        {
            _rules = new List<IRule>
            {
                new Rules.MissingControllerRule(),
                new Rules.MissingLineRule(),
                new Rules.MissingAddressRule(),
                new Rules.DuplicateAddressRule(),
                new Rules.LineAddressOverCapacityRule(),
                new Rules.LineCurrentOverCapacityRule(),
            };
        }

        public void Evaluate(ModuleData data)
        {
            data.RuleResults.Clear();

            foreach (IRule rule in _rules)
            {
                var results = rule.Evaluate(data);
                if (results != null && results.Count > 0)
                {
                    data.RuleResults.AddRange(results);
                }
            }
        }
    }
}
