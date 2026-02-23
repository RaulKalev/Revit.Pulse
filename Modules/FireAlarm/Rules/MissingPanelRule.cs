using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Flags devices that have no panel assignment or are assigned to the placeholder "(No Panel)".
    /// </summary>
    public class MissingPanelRule : IRule
    {
        public string Name => "MissingPanel";
        public string Description => "Device has no panel assignment.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();

            foreach (var device in data.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.PanelId) || device.PanelId.Contains("(No Panel)"))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Device '{device.DisplayName}' has no panel assignment.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
