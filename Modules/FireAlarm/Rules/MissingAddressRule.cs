using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Flags devices that have no address assigned.
    /// </summary>
    public class MissingAddressRule : IRule
    {
        public string Name => "MissingAddress";
        public string Description => "Device has no address.";
        public Severity DefaultSeverity => Severity.Warning;

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

                if (string.IsNullOrWhiteSpace(device.Address))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"Device '{device.DisplayName}' has no address.",
                        device.RevitElementId,
                        device.EntityId));
                }
            }

            return results;
        }
    }
}
