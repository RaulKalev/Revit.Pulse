using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Detects sounder/NAC devices that are assigned to neither a loop nor a SubCircuit.
    ///
    /// Sounder-category devices that have no loop assignment AND are not members of
    /// any SubCircuit are logically orphaned — they will not be triggered or monitored
    /// by any part of the fire alarm topology.
    ///
    /// Device type matching is keyword-based so that it works with any naming convention
    /// without requiring a hardcoded device category.
    ///
    /// Severity: Warning.
    /// </summary>
    public class SubCircuitOrphanSounderRule : IRule
    {
        // Keywords that suggest a device is a notification/sounder type.
        private static readonly string[] _sounderKeywords =
        {
            "sounder", "nac", "notification", "horn", "bell", "siren",
            "speaker", "strobe", "annunciator", "buzzer"
        };

        public string Name => "SubCircuitOrphanSounder";
        public string Description => "Sounder/NAC device is not assigned to any loop or SubCircuit.";
        public Severity DefaultSeverity => Severity.Warning;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();

            if (data.SubCircuits == null)
                return results;

            // Build the set of device element IDs covered by any SubCircuit.
            var coveredBySubCircuit = new HashSet<int>();
            foreach (var sc in data.SubCircuits)
                foreach (int id in sc.DeviceElementIds)
                    coveredBySubCircuit.Add(id);

            foreach (var device in data.Devices)
            {
                // Is this device a sounder-type?
                string typeLabel = (device.DeviceType ?? string.Empty).ToLowerInvariant();
                bool isSounder = false;
                foreach (string keyword in _sounderKeywords)
                {
                    if (typeLabel.Contains(keyword))
                    {
                        isSounder = true;
                        break;
                    }
                }
                if (!isSounder) continue;

                // Sounder on a loop → fine (loop handles monitoring).
                bool hasLoop = !string.IsNullOrWhiteSpace(device.LoopId)
                               && !device.LoopId.Contains("(No Loop)");
                if (hasLoop) continue;

                // Sounder in a SubCircuit → fine.
                bool inSubCircuit = device.RevitElementId.HasValue
                    && coveredBySubCircuit.Contains((int)device.RevitElementId.Value);
                if (inSubCircuit) continue;

                results.Add(new RuleResult(
                    Name,
                    DefaultSeverity,
                    $"Sounder/NAC device '{device.DisplayName}' (type: '{device.DeviceType}') " +
                    "is not assigned to any loop or SubCircuit. " +
                    "Assign it to a SubCircuit or a loop to ensure it is triggered during an alarm.",
                    device.RevitElementId,
                    device.EntityId));
            }

            return results;
        }
    }
}
