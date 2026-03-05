using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Flags device element IDs that appear in more than one SubCircuit.
    ///
    /// A device should belong to at most one SubCircuit.
    /// Duplicate membership typically indicates a data consistency issue caused
    /// by manual JSON edits or a failed assign/remove operation.
    /// </summary>
    public class SubCircuitDuplicateMemberRule : IRule
    {
        public string Name => "SubCircuitDuplicateMember";
        public string Description => "A device is assigned to more than one SubCircuit.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var fa = data.GetPayload<FireAlarmPayload>();
            if (fa == null) return results;

            if (fa.SubCircuits == null || fa.SubCircuits.Count < 2)
                return results;

            // Count how many SubCircuits each device element ID appears in.
            var membershipCount = new Dictionary<int, List<string>>(); // elementId → subCircuit names

            foreach (var sc in fa.SubCircuits)
            {
                foreach (int elemId in sc.DeviceElementIds)
                {
                    if (!membershipCount.TryGetValue(elemId, out var names))
                        membershipCount[elemId] = names = new List<string>();
                    names.Add(sc.Name ?? sc.Id);
                }
            }

            // Build a reverse lookup: Revit ElementId → device EntityId (for entityId on the result)
            var entityIdByElementId = new Dictionary<int, string>();
            foreach (var device in fa.Devices)
            {
                if (device.RevitElementId.HasValue)
                    entityIdByElementId[(int)device.RevitElementId.Value] = device.EntityId;
            }

            foreach (var kvp in membershipCount)
            {
                if (kvp.Value.Count <= 1) continue;

                entityIdByElementId.TryGetValue(kvp.Key, out string entityId);
                string circuitList = string.Join(", ", kvp.Value);

                results.Add(new RuleResult(
                    Name,
                    DefaultSeverity,
                    $"Device (ElementId {kvp.Key}) is assigned to {kvp.Value.Count} SubCircuits: {circuitList}. " +
                    "Each device may belong to only one SubCircuit.",
                    elementId: kvp.Key,
                    entityId: entityId));
            }

            return results;
        }
    }
}
