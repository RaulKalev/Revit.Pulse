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
            var fa = data.GetPayload<FireAlarmPayload>();
            if (fa == null) return results;

            var nacMemberIds = new System.Collections.Generic.HashSet<long>();
            foreach (var sc in fa.SubCircuits)
                foreach (var id in sc.DeviceElementIds)
                    nacMemberIds.Add(id);

            foreach (var device in fa.Devices)
            {
                if (device.RevitElementId.HasValue && nacMemberIds.Contains(device.RevitElementId.Value))
                    continue;

                if (SubCircuitRuleHelpers.IsSounderType(device.DeviceType))
                    continue;

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
