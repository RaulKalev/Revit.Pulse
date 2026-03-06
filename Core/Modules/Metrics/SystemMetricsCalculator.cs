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

        // ── SubCircuit V-Drop Health ──────────────────────────────────────────

        private const double CopperRho20 = 0.0175; // Ω·mm²/m at 20 °C

        /// <summary>
        /// Returns a <see cref="HealthIssueItem"/> for each SubCircuit whose computed
        /// end-of-circuit voltage would fall below the configured minimum device voltage
        /// (default 16 V — typical UL/EN minimum for NAC appliances).
        /// Requires assignment + wire-config data so is called from the ViewModel layer,
        /// not the rule-pack pipeline.
        /// </summary>
        public static List<HealthIssueItem> ComputeSubCircuitVDropIssues(
            ModuleData data,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            var result = new List<HealthIssueItem>();
            if (data == null || assignments == null || deviceStore == null) return result;

            foreach (var sc in data.GetPayload<Pulse.Modules.FireAlarm.FireAlarmPayload>()?.SubCircuits
                              ?? System.Linq.Enumerable.Empty<SubCircuit>())
            {
                // Must have a wire type assigned
                if (string.IsNullOrEmpty(sc.WireTypeKey)) continue;

                var wire = deviceStore.Wires.FirstOrDefault(w =>
                    string.Equals(w.Name, sc.WireTypeKey, StringComparison.OrdinalIgnoreCase));
                if (wire == null) continue;
                if (wire.CoreSizeMm2 <= 0 && wire.ResistancePerMetreOhm <= 0) continue;

                double rPerMetreAt20 = wire.ResistancePerMetreOhm > 0
                    ? wire.ResistancePerMetreOhm
                    : CopperRho20 / wire.CoreSizeMm2;
                if (rPerMetreAt20 <= 0) continue;

                // Temperature derating
                double rPerMetre = rPerMetreAt20 * (1.0 + 0.00393 * (sc.CableTemperatureDegC - 20.0));

                // Find the corresponding topology node for cable length + mA + nominal voltage
                string nodeId = "subcircuit::" + sc.Id;
                var node = data.Nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node == null) continue;

                if (!node.Properties.TryGetValue("CableLength", out string clStr)) continue;
                if (!double.TryParse(clStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double cableLengthMetres) || cableLengthMetres <= 0) continue;

                if (!node.Properties.TryGetValue("TotalMaAlarm", out string maStr)) continue;
                if (!double.TryParse(maStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double maAlarm) || maAlarm <= 0) continue;

                if (!node.Properties.TryGetValue("NominalVoltage", out string nomVStr)) continue;
                if (!double.TryParse(nomVStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double nomVolts) || nomVolts <= 0) continue;

                // Worst-case V-drop (all load at far end)
                double vDrop = (maAlarm / 1000.0) * 2.0 * rPerMetre * cableLengthMetres;
                double endVoltage = nomVolts - vDrop;

                if (endVoltage < sc.MinDeviceVoltageV)
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "SubCircuitLowVoltage",
                        Description = $"NAC \"{sc.Name ?? sc.Id}\" end-of-line voltage " +
                                      $"{endVoltage:F1} V is below minimum {sc.MinDeviceVoltageV:F1} V " +
                                      $"(V-drop {vDrop:F1} V on {nomVolts:F0} V supply)",
                        Count       = 1,
                        Status      = HealthStatus.Warning,
                    });
                }
            }

            return result;
        }

        // ── Battery / PSU ─────────────────────────────────────────────────────

        /// <summary>
        /// Computes battery sizing metrics for the given FACP panel.
        /// Returns null when no config is assigned OR when BatteryUnitAh is zero.
        /// </summary>
        public static BatteryMetrics ComputePanelBatteryMetrics(
            Panel panel,
            FireAlarmPayload fa,
            Core.Settings.ControlPanelConfig cfg)
        {
            if (panel == null || fa == null || cfg == null) return null;
            if (cfg.BatteryUnitAh <= 0) return null;

            var devices = panel.Loops.SelectMany(l => l.Devices).ToList();

            double standbyMa = devices.Sum(d => d.CurrentDraw ?? 0.0);
            double alarmMa   = devices.Sum(d => ParseAlarmCurrent(d));

            return new BatteryMetrics
            {
                StandbyCurrentMa    = standbyMa,
                AlarmCurrentMa      = alarmMa,
                BatteryUnitAh       = cfg.BatteryUnitAh,
                OutputCurrentA      = cfg.PsuOutputCurrentA,
                RequiredStandbyHours= cfg.RequiredStandbyHours,
                RequiredAlarmMinutes= cfg.RequiredAlarmMinutes,
                SafetyFactor        = cfg.BatterySafetyFactor > 0 ? cfg.BatterySafetyFactor : 1.0,
                IsConfigured        = true,
            };
        }

        /// <summary>
        /// Computes battery sizing metrics for a group of SubCircuits assigned to the same NAC PSU config.
        /// Returns null when BatteryUnitAh is zero.
        /// </summary>
        public static BatteryMetrics ComputePsuBatteryMetrics(
            System.Collections.Generic.IEnumerable<SubCircuit> subCircuits,
            FireAlarmPayload fa,
            Core.Settings.PsuConfig cfg)
        {
            if (subCircuits == null || fa == null || cfg == null) return null;
            if (cfg.BatteryUnitAh <= 0) return null;

            // Build device lookup by Revit element ID
            var deviceById = new System.Collections.Generic.Dictionary<long, AddressableDevice>();
            foreach (var d in fa.Devices)
                if (d.RevitElementId.HasValue)
                    deviceById[d.RevitElementId.Value] = d;

            double standbyMa = 0.0;
            double alarmMa   = 0.0;

            foreach (var sc in subCircuits)
            {
                foreach (var id in sc.DeviceElementIds)
                {
                    if (!deviceById.TryGetValue(id, out var device)) continue;
                    standbyMa += device.CurrentDraw ?? 0.0;
                    alarmMa   += ParseAlarmCurrent(device);
                }
            }

            return new BatteryMetrics
            {
                StandbyCurrentMa    = standbyMa,
                AlarmCurrentMa      = alarmMa,
                BatteryUnitAh       = cfg.BatteryUnitAh,
                BatteryVoltageV     = cfg.VoltageV,
                OutputCurrentA      = cfg.OutputCurrentA,
                RequiredStandbyHours= cfg.RequiredStandbyHours,
                RequiredAlarmMinutes= cfg.RequiredAlarmMinutes,
                SafetyFactor        = cfg.BatterySafetyFactor > 0 ? cfg.BatterySafetyFactor : 1.0,
                IsConfigured        = true,
            };
        }

        /// <summary>
        /// Returns <see cref="HealthIssueItem"/> entries for battery and PSU issues
        /// across all panels and NAC PSUs with configs assigned.
        /// Follows the same pattern as <see cref="ComputeSubCircuitVDropIssues"/> —
        /// intended to be merged into the health section by the ViewModel.
        /// </summary>
        public static List<HealthIssueItem> ComputeBatteryHealthIssues(
            ModuleData data,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            var result = new List<HealthIssueItem>();
            if (data == null || assignments == null || deviceStore == null) return result;

            var fa = data.GetPayload<FireAlarmPayload>();
            if (fa == null) return result;

            // ── Per-panel (FACP) battery check ────────────────────────────────
            foreach (var panel in fa.Panels)
            {
                string panelLabel = panel.DisplayName;

                if (!assignments.PanelAssignments.TryGetValue(panelLabel, out string cfgName)
                    || string.IsNullOrEmpty(cfgName))
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "BatteryPanelUnconfigured",
                        Description = $"Panel \"{panelLabel}\" — no device config assigned; battery check skipped",
                        Count       = 0,
                        Status      = HealthStatus.Ok,
                    });
                    continue;
                }

                var cfg = deviceStore.ControlPanels.FirstOrDefault(
                    p => string.Equals(p.Name, cfgName, StringComparison.OrdinalIgnoreCase));
                if (cfg == null) continue;

                if (cfg.BatteryUnitAh <= 0)
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "BatteryPanelUnconfigured",
                        Description = $"Panel \"{panelLabel}\" — battery unit size not set in config \"{cfgName}\"",
                        Count       = 0,
                        Status      = HealthStatus.Ok,
                    });
                    continue;
                }

                var metrics = ComputePanelBatteryMetrics(panel, fa, cfg);
                if (metrics == null) continue;

                result.Add(new HealthIssueItem
                {
                    RuleName    = "BatteryPanelNeeded",
                    Description = $"Panel \"{panelLabel}\" — {metrics.BatteriesNeeded}\u00d7 {metrics.BatteryUnitAh:F1} Ah batteries needed "
                                + $"(req. {metrics.RequiredCapacityAh:F2} Ah · {metrics.StandardSummary})",
                    Count       = 0,
                    Status      = HealthStatus.Ok,
                });

                if (metrics.IsPsuOverloaded)
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "BatteryPanelPsuOverload",
                        Description = $"Panel \"{panelLabel}\" PSU overloaded in alarm — "
                                    + $"{Math.Round(metrics.AlarmCurrentMa)} mA load exceeds "
                                    + $"{Math.Round(metrics.OutputCurrentA * 1000)} mA output",
                        Count       = 1,
                        Status      = HealthStatus.Error,
                    });
                }
            }

            // ── Per-PSU (NAC) battery check ───────────────────────────────────
            var faDevConfig = Core.Settings.DeviceConfigService.LoadModuleConfig<Pulse.Modules.FireAlarm.FireAlarmDeviceConfig>(deviceStore, "FireAlarm");
            if (faDevConfig == null) return result;

            // Group SubCircuits by their assigned PsuConfig name
            var scsByPsuName = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.List<SubCircuit>>(StringComparer.OrdinalIgnoreCase);

            foreach (var sc in fa.SubCircuits)
            {
                if (!assignments.SubCircuitPsuAssignments.TryGetValue(sc.Id, out string psuName)
                    || string.IsNullOrEmpty(psuName))
                    continue;

                if (!scsByPsuName.TryGetValue(psuName, out var list))
                    scsByPsuName[psuName] = list = new System.Collections.Generic.List<SubCircuit>();
                list.Add(sc);
            }

            foreach (var kvp in scsByPsuName)
            {
                string psuName = kvp.Key;
                var psuCfg = faDevConfig.PsuUnits.FirstOrDefault(
                    p => string.Equals(p.Name, psuName, StringComparison.OrdinalIgnoreCase));

                if (psuCfg == null) continue;

                if (psuCfg.BatteryUnitAh <= 0)
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "BatteryPsuUnconfigured",
                        Description = $"NAC PSU \"{psuName}\" — battery unit size not set in config",
                        Count       = 0,
                        Status      = HealthStatus.Ok,
                    });
                    continue;
                }

                var metrics = ComputePsuBatteryMetrics(kvp.Value, fa, psuCfg);
                if (metrics == null) continue;

                result.Add(new HealthIssueItem
                {
                    RuleName    = "BatteryPsuNeeded",
                    Description = $"NAC PSU \"{psuName}\" — {metrics.BatteriesNeeded}\u00d7 {metrics.BatteryUnitAh:F1} Ah batteries needed "
                                + $"(req. {metrics.RequiredCapacityAh:F2} Ah · {metrics.StandardSummary})",
                    Count       = 0,
                    Status      = HealthStatus.Ok,
                });

                if (metrics.IsPsuOverloaded)
                {
                    result.Add(new HealthIssueItem
                    {
                        RuleName    = "BatteryPsuOverload",
                        Description = $"NAC PSU \"{psuName}\" overloaded in alarm — "
                                    + $"{Math.Round(metrics.AlarmCurrentMa)} mA load exceeds "
                                    + $"{Math.Round(metrics.OutputCurrentA * 1000)} mA output",
                        Count       = 1,
                        Status      = HealthStatus.Error,
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Parses the alarm-mode current draw for a device.
        /// Reads "_CurrentDrawAlarm" from Properties first; falls back to standby CurrentDraw
        /// (conservative — treats both modes as equal when alarm draw is unknown).
        /// </summary>
        private static double ParseAlarmCurrent(AddressableDevice device)
        {
            if (device.Properties.TryGetValue("_CurrentDrawAlarm", out string alarmStr)
                && !string.IsNullOrEmpty(alarmStr)
                && double.TryParse(alarmStr,
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out double alarmMa))
            {
                return alarmMa;
            }
            return device.CurrentDraw ?? 0.0;
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
