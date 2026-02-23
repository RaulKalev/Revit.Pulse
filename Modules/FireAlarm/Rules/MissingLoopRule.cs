using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Flags devices that have no loop assignment or are assigned to the placeholder "(No Loop)".
    /// </summary>
    public class MissingLoopRule : IRule
    {
        public string Name => "MissingLoop";
        public string Description => "Device has no loop assignment.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();

            foreach (var device in data.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.LoopId) || device.LoopId.Contains("(No Loop)"))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Device '{device.DisplayName}' has no loop assignment.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
