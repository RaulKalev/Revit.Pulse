using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;
using Pulse.Modules.FireAlarm;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Builds a structured English prompt describing the current fire alarm system
    /// configuration.  The prompt can be pasted into an AI assistant for compliance
    /// review or optimisation advice.
    ///
    /// No AI API is called — this is prompt-export only.
    /// </summary>
    public static class SystemCheckPromptBuilder
    {
        /// <summary>
        /// Generate the prompt for the full system (all panels and loops).
        /// </summary>
        public static string Build(
            ModuleData data,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            if (data == null) return string.Empty;

            var sb = new StringBuilder();

            sb.AppendLine("You are a fire alarm system design reviewer.");
            sb.AppendLine("Analyze the following system configuration for compliance, capacity, and design quality.");
            sb.AppendLine();

            int totalDevices = data.Devices.Count;
            int totalErrors  = data.ErrorCount;
            int totalWarnings = data.WarningCount;

            sb.AppendLine($"System Summary:");
            sb.AppendLine($"- Total devices: {totalDevices}");
            sb.AppendLine($"- Panels: {data.Panels.Count}");
            sb.AppendLine($"- Loops: {data.Loops.Count}");
            sb.AppendLine($"- Rule violations: {totalErrors} errors, {totalWarnings} warnings");
            sb.AppendLine();

            foreach (var panel in data.Panels)
            {
                sb.AppendLine($"Panel: {panel.DisplayName}");
                sb.AppendLine($"  Total Loops: {panel.Loops.Count}");

                // Panel config
                if (assignments.PanelAssignments.TryGetValue(panel.DisplayName, out string panelConfigName)
                    && !string.IsNullOrEmpty(panelConfigName))
                {
                    sb.AppendLine($"  Assigned Config: {panelConfigName}");
                    var cfg = deviceStore.ControlPanels.FirstOrDefault(c => c.Name == panelConfigName);
                    if (cfg != null)
                    {
                        int loopCount = Math.Max(panel.Loops.Count, 1);
                        int addrMax   = cfg.MaxAddresses > 0 ? cfg.MaxAddresses : cfg.AddressesPerLoop * loopCount;
                        double maMax  = cfg.MaxMaPerLoop * loopCount;
                        int addrUsed  = panel.Loops.Sum(l => l.Devices.Count);
                        double maUsed = panel.Loops.Sum(l => l.Devices.Sum(d => d.CurrentDraw ?? 0));

                        sb.AppendLine($"  Addresses: {addrUsed} / {addrMax} ({(addrMax > 0 ? Math.Round((double)addrUsed / addrMax * 100) : 0)}%)");
                        sb.AppendLine($"  mA load:   {Math.Round(maUsed)} / {Math.Round(maMax)} mA ({(maMax > 0 ? Math.Round(maUsed / maMax * 100) : 0)}%)");
                    }
                }

                sb.AppendLine();

                foreach (var loop in panel.Loops)
                {
                    AppendLoop(sb, loop, assignments, deviceStore, data, panel);
                }

                // Distribution for this panel
                var panelDevices = panel.Loops.SelectMany(l => l.Devices).ToList();
                if (panelDevices.Count > 0)
                {
                    AppendDistribution(sb, panelDevices, "  ");
                }

                sb.AppendLine();
            }

            // Health warnings
            sb.AppendLine("Health Issues:");
            var issues = SystemMetricsCalculator.ComputeHealthIssues(data);
            bool hasIssues = false;
            foreach (var issue in issues)
            {
                if (issue.Count > 0)
                {
                    hasIssues = true;
                    sb.AppendLine($"  - [{issue.Status.ToString().ToUpperInvariant()}] {issue.Description}: {issue.Count} occurrence(s)");
                }
            }
            if (!hasIssues) sb.AppendLine("  - No issues detected.");

            sb.AppendLine();
            sb.AppendLine("Please review the above system for:");
            sb.AppendLine("1. Compliance with NFPA 72 / EN 54 standards (or applicable regional standard)");
            sb.AppendLine("2. Capacity headroom adequacy");
            sb.AppendLine("3. Loop balance / device distribution");
            sb.AppendLine("4. Cable length concerns");
            sb.AppendLine("5. Any optimisation recommendations");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────

        private static void AppendLoop(
            StringBuilder sb,
            Loop loop,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore,
            ModuleData data,
            Panel parentPanel)
        {
            sb.AppendLine($"  Loop: {loop.DisplayName}");
            sb.AppendLine($"    Devices: {loop.Devices.Count}");

            if (assignments.LoopAssignments.TryGetValue(loop.DisplayName, out string loopConfig)
                && !string.IsNullOrEmpty(loopConfig))
            {
                var cfg = deviceStore.LoopModules.FirstOrDefault(m => m.Name == loopConfig);
                if (cfg != null)
                {
                    int    addrUsed = loop.Devices.Count;
                    double maUsed   = loop.Devices.Sum(d => d.CurrentDraw ?? 0);
                    sb.AppendLine($"    Addresses: {addrUsed} / {cfg.AddressesPerLoop} ({(cfg.AddressesPerLoop > 0 ? Math.Round((double)addrUsed / cfg.AddressesPerLoop * 100) : 0)}%)");
                    sb.AppendLine($"    mA load:   {Math.Round(maUsed)} / {cfg.MaxMaPerLoop} mA ({(cfg.MaxMaPerLoop > 0 ? Math.Round(maUsed / cfg.MaxMaPerLoop * 100) : 0)}%)");
                }
            }

            // Cable length
            try
            {
                var cable = CableLengthCalculator.Calculate(loop, parentPanel);
                sb.AppendLine($"    Cable length: {cable.TotalLengthMetres:F1} m");
            }
            catch { /* skip if routing fails */ }

            // Rule violations on this loop
            var loopIssues = data.RuleResults
                .Where(r => r.EntityId == loop.EntityId
                         || loop.Devices.Any(d => d.EntityId == r.EntityId))
                .ToList();

            int dupes  = loopIssues.Count(r => r.RuleName == "DuplicateAddress");
            int noAddr = loopIssues.Count(r => r.RuleName == "MissingAddress");
            if (dupes > 0)  sb.AppendLine($"    Duplicate addresses: {dupes}");
            if (noAddr > 0) sb.AppendLine($"    Devices without address: {noAddr}");

            // Wire type
            string key = $"{parentPanel.DisplayName}::{loop.DisplayName}";
            if (assignments.LoopWireAssignments.TryGetValue(key, out string wire)
                && !string.IsNullOrEmpty(wire))
            {
                sb.AppendLine($"    Wire type: {wire}");
            }

            // Distribution for this loop
            AppendDistribution(sb, loop.Devices, "    ");

            sb.AppendLine();
        }

        private static void AppendDistribution(StringBuilder sb, IEnumerable<AddressableDevice> devices, string indent)
        {
            var groups = SystemMetricsCalculator.ComputeDistribution(devices);
            if (groups.Count == 0) return;
            sb.AppendLine($"{indent}Distribution:");
            foreach (var g in groups)
                sb.AppendLine($"{indent}  {g.Name}: {g.Count}");
        }
    }
}
