using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Rule pack containing all Fire Alarm validation rules.
    /// Evaluates each rule against the module data and appends results.
    /// </summary>
    public class FireAlarmRulePack : IRulePack
    {
        private readonly List<IRule> _rules;

        public IReadOnlyList<IRule> Rules => _rules;

        public FireAlarmRulePack()
        {
            _rules = new List<IRule>
            {
                new Rules.MissingPanelRule(),
                new Rules.MissingLoopRule(),
                new Rules.MissingAddressRule(),
                new Rules.DuplicateAddressRule(),
                new Rules.MissingRequiredParameterRule(),
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
