using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags lighting devices that have no controller assignment or are assigned
    /// to the placeholder "(No Controller)".
    /// </summary>
    public class MissingControllerRule : IRule
    {
        public string Name => "MissingController";
        public string Description => "Device has no controller assignment.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (var device in lg.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.PanelId) || device.PanelId.Contains("(No Controller)"))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Luminaire '{device.DisplayName}' has no controller assignment.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
