using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Flags SubCircuits whose host Output Module device is not assigned to any loop.
    ///
    /// A SubCircuit that has no host on a monitored loop will never be triggered
    /// during a fire condition.
    /// </summary>
    public class SubCircuitMissingTriggerRule : IRule
    {
        public string Name => "SubCircuitMissingTrigger";
        public string Description => "SubCircuit host Output Module is not assigned to any loop.";
        public Severity DefaultSeverity => Severity.Warning;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();

            if (data.SubCircuits == null || data.SubCircuits.Count == 0)
                return results;

            // Build quick lookup: Revit ElementId → LoopId
            var loopByElementId = new Dictionary<long, string>();
            foreach (var device in data.Devices)
            {
                if (device.RevitElementId.HasValue)
                    loopByElementId[device.RevitElementId.Value] = device.LoopId ?? string.Empty;
            }

            foreach (var sc in data.SubCircuits)
            {
                // Try to find the host device by its RevitElementId
                if (!loopByElementId.TryGetValue(sc.HostElementId, out string loopId)
                    || string.IsNullOrWhiteSpace(loopId)
                    || loopId.Contains("(No Loop)"))
                {
                    results.Add(new RuleResult(
                        Name,
                        DefaultSeverity,
                        $"SubCircuit '{sc.Name}' host element (ID {sc.HostElementId}) is not assigned to any loop. " +
                        "This SubCircuit will not be triggered during a fire alarm.",
                        elementId: null,
                        entityId: "subcircuit::" + sc.Id));
                }
            }

            return results;
        }
    }
}
