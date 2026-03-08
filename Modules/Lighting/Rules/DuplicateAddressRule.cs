using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.Lighting.Rules
{
    /// <summary>
    /// Flags devices that share the same address within the same line.
    /// Duplicate addresses on the same DALI line cause communication conflicts.
    /// </summary>
    public class DuplicateAddressRule : IRule
    {
        public string Name => "DuplicateAddress";
        public string Description => "Multiple devices share the same address on a line.";
        public Severity DefaultSeverity => Severity.Error;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();
            var lg = data.GetPayload<LightingPayload>();
            if (lg == null) return results;

            foreach (Loop line in lg.Lines)
            {
                var addressMap = new Dictionary<string, List<AddressableDevice>>(System.StringComparer.OrdinalIgnoreCase);

                foreach (var device in line.Devices)
                {
                    if (string.IsNullOrWhiteSpace(device.Address))
                        continue; // Caught by MissingAddressRule

                    string normalizedAddress = device.Address.Trim();
                    if (!addressMap.TryGetValue(normalizedAddress, out var list))
                    {
                        list = new List<AddressableDevice>();
                        addressMap[normalizedAddress] = list;
                    }
                    list.Add(device);
                }

                // Flag all devices that share an address
                foreach (var kvp in addressMap)
                {
                    if (kvp.Value.Count > 1)
                    {
                        foreach (var device in kvp.Value)
                        {
                            results.Add(new RuleResult(
                                Name,
                                DefaultSeverity,
                                $"Address '{kvp.Key}' is duplicated on line '{line.DisplayName}' — luminaire '{device.DisplayName}'.",
                                device.RevitElementId,
                                device.EntityId));
                        }
                    }
                }
            }

            return results;
        }
    }
}
