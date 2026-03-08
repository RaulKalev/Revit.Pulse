using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags lines where the number of addressed devices exceeds the maximum
    /// address capacity for the lighting system (default: 64 for DALI).
    /// </summary>
    public class LineAddressOverCapacityRule : IRule
    {
        /// <summary>DALI default: 64 addresses per line.</summary>
        private const int DefaultMaxAddresses = 64;

        public string Name => "LineAddressOverCapacity";
        public string Description => "Line has more addressed devices than the maximum capacity.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (Loop line in lg.Lines)
            {
                int addressedCount = line.Devices.Count(d => !string.IsNullOrWhiteSpace(d.Address));
                if (addressedCount > DefaultMaxAddresses)
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Line '{line.DisplayName}' has {addressedCount} addressed devices, exceeding the maximum of {DefaultMaxAddresses}.",
                        line.RevitElementId,
                        line.EntityId));
                }
            }

            return results;
        }
    }
}
