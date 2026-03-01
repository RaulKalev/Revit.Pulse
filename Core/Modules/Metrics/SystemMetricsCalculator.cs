using System;
using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;
using Pulse.Modules.FireAlarm;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Stateless calculator that derives all Metrics-panel data from raw module data.
    /// Keeps business logic out of the ViewModel and the UI layer.
    /// </summary>
    public static class SystemMetricsCalculator
    {
        // ── Capacity ─────────────────────────────────────────────────────────

        /// <summary>
        /// Compute capacity metrics for the selected panel.  Returns null when no
        /// panel config has been assigned (gauges cannot be shown).
        /// </summary>
        public static CapacityMetrics ComputeForPanel(
            Panel panel,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            if (panel == null) return null;

            if (!assignments.PanelAssignments.TryGetValue(panel.DisplayName, out string assignedName)) return null;

            var cfg = deviceStore.ControlPanels.FirstOrDefault(p => p.Name == assignedName);
            if (cfg == null) return null;

            int loopCount = Math.Max(panel.Loops.Count, 1);
            int addrMax   = cfg.MaxAddresses > 0 ? cfg.MaxAddresses : cfg.AddressesPerLoop * loopCount;

            return new CapacityMetrics
            {
                AddressesMax  = addrMax,
                MaMax         = cfg.MaxMaPerLoop * loopCount,
                AddressesUsed = panel.Loops.Sum(l => l.Devices.Count),
                MaUsed        = panel.Loops.Sum(l => l.Devices.Sum(d => d.CurrentDraw ?? 0))
            };
        }

        /// <summary>
        /// Compute capacity metrics for the selected loop.  Returns null when no
        /// loop module config has been assigned.
        /// </summary>
        public static CapacityMetrics ComputeForLoop(
            Loop loop,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            if (loop == null) return null;

            if (!assignments.LoopAssignments.TryGetValue(loop.DisplayName, out string assignedName)) return null;

            var cfg = deviceStore.LoopModules.FirstOrDefault(m => m.Name == assignedName);
            if (cfg == null) return null;

            return new CapacityMetrics
            {
                AddressesMax  = cfg.AddressesPerLoop,
                MaMax         = cfg.MaxMaPerLoop,
                AddressesUsed = loop.Devices.Count,
                MaUsed        = loop.Devices.Sum(d => d.CurrentDraw ?? 0)
            };
        }

        // ── Health Issues ─────────────────────────────────────────────────────

        private static readonly (string RuleName, string Label, HealthStatus DefaultStatus)[] RuleDescriptions =
        {
            ("DuplicateAddress",         "Duplicate addresses",         HealthStatus.Error),
            ("MissingAddress",           "Unassigned device addresses", HealthStatus.Warning),
            ("MissingLoop",              "Devices with no loop",        HealthStatus.Warning),
            ("MissingPanel",             "Devices with no panel",       HealthStatus.Warning),
            ("MissingRequiredParameter", "Missing required parameters", HealthStatus.Error),
        };

        /// <summary>
        /// Build a health-issue list scoped to a panel, loop, or the whole system.
        /// Passing null for both panel/loop returns system-wide issues.
        /// </summary>
        public static List<HealthIssueItem> ComputeHealthIssues(
            ModuleData data,
            Panel panel = null,
            Loop  loop  = null)
        {
            if (data == null) return new List<HealthIssueItem>();

            // Determine which entity IDs are in scope
            HashSet<string> scopeIds = null;
            if (loop != null)
            {
                scopeIds = new HashSet<string>(
                    loop.Devices.Select(d => d.EntityId),
                    StringComparer.OrdinalIgnoreCase);
                scopeIds.Add(loop.EntityId);
            }
            else if (panel != null)
            {
                scopeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                scopeIds.Add(panel.EntityId);
                foreach (var l in panel.Loops)
                {
                    scopeIds.Add(l.EntityId);
                    foreach (var d in l.Devices) scopeIds.Add(d.EntityId);
                }
            }

            var result = new List<HealthIssueItem>();

            foreach (var (ruleName, label, defaultStatus) in RuleDescriptions)
            {
                var matching = data.RuleResults
                    .Where(r => string.Equals(r.RuleName, ruleName, StringComparison.OrdinalIgnoreCase)
                                && (scopeIds == null || r.EntityId == null || scopeIds.Contains(r.EntityId)))
                    .ToList();

                var elementIds = matching
                    .Where(r => r.ElementId.HasValue)
                    .Select(r => r.ElementId.Value)
                    .Distinct()
                    .ToList();

                HealthStatus status;
                if (matching.Count == 0)
                    status = HealthStatus.Ok;
                else if (matching.Any(r => r.Severity == Severity.Error))
                    status = HealthStatus.Error;
                else
                    status = HealthStatus.Warning;

                result.Add(new HealthIssueItem
                {
                    RuleName           = ruleName,
                    Description        = label,
                    Count              = matching.Count,
                    Status             = status,
                    AffectedElementIds = elementIds,
                });
            }

            // Also check near-capacity loops (not a rule — derived metric)
            if (panel != null || loop == null)
            {
                // Only meaningful when we have assignments (done in ViewModel layer)
                // Reserve slot so ViewModel can inject it
            }

            return result;
        }

        /// <summary>
        /// Derives capacity-related health-issue items from a <see cref="CapacityMetrics"/> snapshot.
        /// Returns an empty list when capacity is within normal limits.
        /// </summary>
        public static List<HealthIssueItem> ComputeCapacityHealthIssues(CapacityMetrics cap)
        {
            var result = new List<HealthIssueItem>();
            if (cap == null) return result;

            if (cap.AddressStatus == CapacityStatus.Warning)
                result.Add(new HealthIssueItem
                {
                    RuleName    = "CapacityAddresses",
                    Description = $"High address usage \u00b7 {cap.AddressSummary}",
                    Count       = 0,
                    Status      = HealthStatus.Warning,
                });
            else if (cap.AddressStatus == CapacityStatus.Critical)
                result.Add(new HealthIssueItem
                {
                    RuleName    = "CapacityAddresses",
                    Description = $"Critical address usage \u00b7 {cap.AddressSummary}",
                    Count       = 0,
                    Status      = HealthStatus.Error,
                });

            if (cap.MaStatus == CapacityStatus.Warning)
                result.Add(new HealthIssueItem
                {
                    RuleName    = "CapacityMa",
                    Description = $"High mA load \u00b7 {cap.MaSummary}",
                    Count       = 0,
                    Status      = HealthStatus.Warning,
                });
            else if (cap.MaStatus == CapacityStatus.Critical)
                result.Add(new HealthIssueItem
                {
                    RuleName    = "CapacityMa",
                    Description = $"Critical mA load \u00b7 {cap.MaSummary}",
                    Count       = 0,
                    Status      = HealthStatus.Error,
                });

            return result;
        }

        // ── Distribution ──────────────────────────────────────────────────────

        /// <summary>
        /// Group devices by DeviceType for the selected scope.
        /// </summary>
        public static List<DistributionGroup> ComputeDistribution(
            IEnumerable<AddressableDevice> devices)
        {
            if (devices == null) return new List<DistributionGroup>();

            var deviceList = devices.ToList();
            if (deviceList.Count == 0) return new List<DistributionGroup>();

            var groups = deviceList
                .GroupBy(d => string.IsNullOrWhiteSpace(d.DeviceType) ? "(untyped)" : d.DeviceType,
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => new DistributionGroup
                {
                    Name  = g.Key,
                    Count = g.Count(),
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            int total = deviceList.Count;
            foreach (var g in groups)
                g.Fraction = total > 0 ? (double)g.Count / total : 0;

            return groups;
        }

        // ── Cabling ───────────────────────────────────────────────────────────

        /// <summary>
        /// Build cabling metrics using the existing CableLengthCalculator.
        /// Scoped to a single loop or all loops in a panel.
        /// </summary>
        public static CablingMetrics ComputeCabling(
            Panel panel,
            Loop  selectedLoop = null)
        {
            var metrics = new CablingMetrics();
            if (panel == null) return metrics;

            var loopsToProcess = selectedLoop != null
                ? new List<Loop> { selectedLoop }
                : panel.Loops
                    .OrderBy(l => ExtractLeadingInt(l.DisplayName))
                    .ThenBy(l => l.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var loop in loopsToProcess)
            {
                var calc = CableLengthCalculator.Calculate(loop, panel);
                metrics.LoopInfos.Add(new LoopCablingInfo
                {
                    LoopName     = loop.DisplayName,
                    LengthMetres = Math.Round(calc.TotalLengthMetres, 1),
                    DeviceCount  = calc.RoutedDeviceCount,
                });
            }

            return metrics;
        }

        // ── Overall Status ────────────────────────────────────────────────────

        /// <summary>
        /// Derive the overall system status from a health issue list and capacity data.
        /// </summary>
        public static HealthStatus ComputeOverallStatus(
            IEnumerable<HealthIssueItem> issues,
            CapacityMetrics capacity = null)
        {
            if (issues == null) return HealthStatus.Ok;

            if (issues.Any(i => i.Status == HealthStatus.Error))     return HealthStatus.Error;
            if (issues.Any(i => i.Status == HealthStatus.Warning))   return HealthStatus.Warning;
            if (capacity != null && (capacity.AddressStatus == CapacityStatus.Critical
                                  || capacity.MaStatus      == CapacityStatus.Critical))
                return HealthStatus.Error;
            if (capacity != null && (capacity.AddressStatus == CapacityStatus.Warning
                                  || capacity.MaStatus      == CapacityStatus.Warning))
                return HealthStatus.Warning;

            return HealthStatus.Ok;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts the first contiguous run of digits from a string as an integer
        /// for numeric-aware sorting.  Returns <see cref="int.MaxValue"/> when no
        /// digits are found so non-numeric names sort last.
        /// </summary>
        private static int ExtractLeadingInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return int.MaxValue;
            int i = 0;
            while (i < s.Length && !char.IsDigit(s[i])) i++;
            int j = i;
            while (j < s.Length && char.IsDigit(s[j])) j++;
            if (i < j && int.TryParse(s.Substring(i, j - i), out int n)) return n;
            return int.MaxValue;
        }
    }
}
