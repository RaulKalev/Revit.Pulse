using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags lighting devices that have no address assigned.
    /// </summary>
    public class MissingAddressRule : IRule
    {
        public string Name => "MissingAddress";
        public string Description => "Device has no address.";
        public Severity DefaultSeverity => Severity.Warning;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (var device in lg.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.Address))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Luminaire '{device.DisplayName}' has no address.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
