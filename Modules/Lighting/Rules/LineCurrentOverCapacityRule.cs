using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags lines where the total current draw exceeds the maximum bus current
    /// capacity (default: 250 mA for DALI).
    /// </summary>
    public class LineCurrentOverCapacityRule : IRule
    {
        /// <summary>DALI default: 250 mA per line.</summary>
        private const double DefaultMaxMa = 250.0;

        public string Name => "LineCurrentOverCapacity";
        public string Description => "Line total current draw exceeds maximum bus capacity.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (Loop line in lg.Lines)
            {
                double totalMa = line.Devices.Sum(d => d.CurrentDraw ?? 0);
                if (totalMa > DefaultMaxMa)
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Line '{line.DisplayName}' draws {totalMa:F1} mA, exceeding the maximum of {DefaultMaxMa:F0} mA.",
                        line.RevitElementId,
                        line.EntityId));
                }
            }

            return results;
        }
    }
}
