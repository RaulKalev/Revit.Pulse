using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags lighting devices that have no line assignment or are assigned
    /// to the placeholder "(No Line)".
    /// </summary>
    public class MissingLineRule : IRule
    {
        public string Name => "MissingLine";
        public string Description => "Device has no line assignment.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (var device in lg.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.LoopId) || device.LoopId.Contains("(No Line)"))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Luminaire '{device.DisplayName}' has no line assignment.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
