using System.Collections.Generic;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Warns when SubCircuits exist in the project but no PSU fault input monitoring
    /// module is detectable.
    ///
    /// A SubCircuit sounder line should have its PSU monitored by an input module
    /// on a loop.  This rule provides a best-effort warning when SubCircuits are
    /// present but no device with a PSU/fault monitoring role can be found in the
    /// collected data.
    ///
    /// Severity: Warning (advisory — cannot fully validate without explicit device role data).
    /// </summary>
    public class SubCircuitMissingFaultMonitorRule : IRule
    {
        // Keywords that suggest a device is intended as a PSU/fault monitor.
        private static readonly string[] _faultKeywords =
        {
            "psu", "fault", "monitor", "input", "sounder fault", "nac monitor"
        };

        public string Name => "SubCircuitMissingFaultMonitor";
        public string Description => "SubCircuits exist but no PSU fault monitoring input device was found.";
        public Severity DefaultSeverity => Severity.Warning;

        public IReadOnlyList<RuleResult> Evaluate(ModuleData data)
        {
            var results = new List<RuleResult>();

            if (data.SubCircuits == null || data.SubCircuits.Count == 0)
                return results;

            // Check if any device in the topology looks like a PSU/fault monitor.
            bool hasFaultMonitor = false;
            foreach (var device in data.Devices)
            {
                string typeLabel = (device.DeviceType ?? string.Empty).ToLowerInvariant();
                foreach (string keyword in _faultKeywords)
                {
                    if (typeLabel.Contains(keyword))
                    {
                        hasFaultMonitor = true;
                        break;
                    }
                }
                if (hasFaultMonitor) break;
            }

            if (!hasFaultMonitor)
            {
                results.Add(new RuleResult(
                    Name,
                    DefaultSeverity,
                    $"{data.SubCircuits.Count} SubCircuit(s) found but no PSU fault monitoring input module " +
                    "was detected in the system. Ensure a fault input device monitors each PSU/NAC circuit.",
                    elementId: null,
                    entityId: null));
            }

            return results;
        }
    }
}
