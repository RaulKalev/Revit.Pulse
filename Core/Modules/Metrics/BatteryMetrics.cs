using System;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Snapshot of battery / PSU sizing data for one FACP or ancillary NAC PSU.
    ///
    /// Required capacity formula (EN 54-4 / NFPA 72):
    ///   C_req = (I_standby/1000 × t_standby + I_alarm/1000 × t_alarm/60) × f_safety
    ///
    /// The result is used to prescribe how many individual battery units are needed.
    /// EN 54-4 requires a minimum of 2 batteries; this is enforced in BatteriesNeeded.
    /// </summary>
    public sealed class BatteryMetrics
    {
        // ── Inputs ────────────────────────────────────────────────────────────

        /// <summary>Sum of standby (normal) current draw across all supervised devices, in mA.</summary>
        public double StandbyCurrentMa { get; set; }

        /// <summary>Sum of alarm current draw across all supervised devices, in mA.</summary>
        public double AlarmCurrentMa { get; set; }

        /// <summary>Capacity of one individual battery unit in Amp-hours (e.g. 7.2 Ah).</summary>
        public double BatteryUnitAh { get; set; }

        /// <summary>PSU rated output current in Amperes (0 = check disabled).</summary>
        public double OutputCurrentA { get; set; }

        /// <summary>Required standby duration in hours.</summary>
        public double RequiredStandbyHours { get; set; } = 24.0;

        /// <summary>Required alarm duration in minutes.</summary>
        public double RequiredAlarmMinutes { get; set; } = 30.0;

        /// <summary>Safety factor applied to required capacity.</summary>
        public double SafetyFactor { get; set; } = 1.25;

        /// <summary>True when a battery unit size has been configured (non-zero).</summary>
        public bool IsConfigured { get; set; }

        /// <summary>Nominal system voltage in Volts (e.g. 24 V). Used to derive per-unit battery voltage.</summary>
        public double BatteryVoltageV { get; set; } = 24.0;

        // ── Derived ───────────────────────────────────────────────────────────

        /// <summary>
        /// Battery capacity required to meet standby + alarm duration, including safety factor.
        ///   C_req = (I_s/1000 × t_s + I_a/1000 × t_a/60) × f
        /// </summary>
        public double RequiredCapacityAh =>
            (StandbyCurrentMa / 1000.0 * RequiredStandbyHours
             + AlarmCurrentMa  / 1000.0 * RequiredAlarmMinutes / 60.0)
            * SafetyFactor;

        /// <summary>
        /// Number of battery units required, rounded up to meet the required capacity.
        /// Enforces a minimum of 2 per EN 54-4.
        /// </summary>
        public int BatteriesNeeded =>
            BatteryUnitAh > 0
                ? Math.Max(2, (int)Math.Ceiling(RequiredCapacityAh / BatteryUnitAh))
                : 0;

        /// <summary>Total installed capacity based on the prescribed battery count.</summary>
        public double TotalInstalledAh => BatteriesNeeded * BatteryUnitAh;

        /// <summary>Alarm current as a fraction of PSU output (0–1+). Zero when output check is disabled.</summary>
        public double PsuFraction =>
            OutputCurrentA > 0 ? AlarmCurrentMa / 1000.0 / OutputCurrentA : 0;

        /// <summary>True when alarm current exceeds PSU rated output.</summary>
        public bool IsPsuOverloaded =>
            OutputCurrentA > 0 && AlarmCurrentMa / 1000.0 > OutputCurrentA;

        // ── Status ────────────────────────────────────────────────────────────

        /// <summary>
        /// Capacity status — always Normal because we prescribe exactly the right number of batteries.
        /// </summary>
        public CapacityStatus CapacityStatus => Metrics.CapacityStatus.Normal;

        /// <summary>PSU output-current check status.</summary>
        public CapacityStatus PsuStatus
        {
            get
            {
                if (OutputCurrentA <= 0) return Metrics.CapacityStatus.Normal;
                if (PsuFraction >= MetricsThresholds.CriticalFraction) return Metrics.CapacityStatus.Critical;
                if (PsuFraction >= MetricsThresholds.WarningFraction)  return Metrics.CapacityStatus.Warning;
                return Metrics.CapacityStatus.Normal;
            }
        }
        // ── Standard battery recommendation ──────────────────────────────────

        /// <summary>
        /// Standard 12 V VRLA/AGM battery unit sizes available from common fire-alarm suppliers.
        /// EN 54-4 typically uses 2 × 12 V in series for a 24 V system.
        /// </summary>
        private static readonly double[] StandardSizesAh =
        {
            1.2, 2.1, 2.3, 3.2, 4.5, 7.0, 7.2, 12.0, 17.0, 18.0,
            24.0, 26.0, 33.0, 38.0, 40.0, 45.0, 55.0, 65.0, 80.0, 100.0
        };

        /// <summary>
        /// Smallest standard battery unit size (Ah) that, when used in the prescribed quantity,
        /// meets RequiredCapacityAh.  EN 54-4 minimum is 2 batteries.
        /// Returns 0 when required capacity is 0.
        /// </summary>
        public double RecommendedBatteryUnitAh
        {
            get
            {
                double reqAh = RequiredCapacityAh;
                if (reqAh <= 0) return 0;
                // Prefer even battery counts (2, 4, 6…). For each even count try sizes
                // smallest-first; the first (count, size) pair that covers reqAh wins.
                for (int count = 2; count <= 40; count += 2)
                    foreach (double size in StandardSizesAh)
                        if (count * size >= reqAh) return size;
                return StandardSizesAh[StandardSizesAh.Length - 1];
            }
        }

        /// <summary>
        /// Number of batteries of the recommended standard size needed (always even, minimum 2).
        /// </summary>
        public int RecommendedBatteryCount
        {
            get
            {
                double reqAh = RequiredCapacityAh;
                if (reqAh <= 0) return 0;
                for (int count = 2; count <= 40; count += 2)
                    foreach (double size in StandardSizesAh)
                        if (count * size >= reqAh) return count;
                // Fallback: round up to next even count using the largest standard size
                double maxSize = StandardSizesAh[StandardSizesAh.Length - 1];
                int raw = (int)Math.Ceiling(reqAh / maxSize);
                return Math.Max(2, raw % 2 == 0 ? raw : raw + 1);
            }
        }

        /// <summary>e.g. "2× 7.2 Ah  (req. 8.25 Ah)"</summary>
        public string RecommendedBatterySummary =>
            RecommendedBatteryUnitAh > 0
                ? $"{RecommendedBatteryCount}\u00d7 {RecommendedBatteryUnitAh:F1} Ah  (req. {RequiredCapacityAh:F2} Ah)"
                : "—";
        // ── Display strings ───────────────────────────────────────────────────

        /// <summary>e.g. "2× 12V/12.0Ah = 24.0 Ah  (req. 15.47 Ah)" — uses formula-based standard size</summary>
        public string RecommendedCapacitySummary
        {
            get
            {
                double unitAh = RecommendedBatteryUnitAh;
                if (unitAh <= 0) return "— batteries";
                int count = RecommendedBatteryCount;
                double unitV = BatteryVoltageV > 0 ? BatteryVoltageV / 2.0 : 12.0;
                return $"{count}\u00d7 {unitV:F0}V/{unitAh:F1}Ah = {count * unitAh:F1} Ah  (req. {RequiredCapacityAh:F2} Ah)";
            }
        }

        /// <summary>
        /// Four-step formula breakdown showing the EN 54-4 capacity equation,
        /// values substituted, terms evaluated, and the final required Ah.
        /// </summary>
        public string FormulaBreakdown
        {
            get
            {
                double stdTerm = StandbyCurrentMa / 1000.0 * RequiredStandbyHours;
                double almTerm = AlarmCurrentMa   / 1000.0 * RequiredAlarmMinutes / 60.0;
                double reqAh   = RequiredCapacityAh;
                return
                    $"C = (Is/1000 × ts + Ia/1000 × ta/60) × f\n" +
                    $"  = ({StandbyCurrentMa:F0}/1000 × {RequiredStandbyHours:F0} + {AlarmCurrentMa:F0}/1000 × {RequiredAlarmMinutes:F0}/60) × {SafetyFactor:F2}\n" +
                    $"  = ({stdTerm:F3} + {almTerm:F3}) × {SafetyFactor:F2}\n" +
                    $"  = {reqAh:F2} Ah";
            }
        }

        /// <summary>e.g. "2× 7.2 Ah = 14.4 Ah (req. 8.25 Ah)"</summary>
        public string CapacitySummary =>
            BatteryUnitAh > 0
                ? $"{BatteriesNeeded}\u00d7 {BatteryUnitAh:F1} Ah = {TotalInstalledAh:F1} Ah  (req. {RequiredCapacityAh:F2} Ah)"
                : "— batteries (not configured)";

        /// <summary>e.g. "250 mA standby"</summary>
        public string StandbyCurrentSummary => $"{Math.Round(StandbyCurrentMa)} mA standby";

        /// <summary>e.g. "750 mA alarm"</summary>
        public string AlarmCurrentSummary => $"{Math.Round(AlarmCurrentMa)} mA alarm";

        /// <summary>e.g. "750 / 500 mA (150 %)"</summary>
        public string PsuSummary =>
            OutputCurrentA > 0
                ? $"{Math.Round(AlarmCurrentMa)} / {Math.Round(OutputCurrentA * 1000)} mA ({Math.Round(PsuFraction * 100)} %)"
                : string.Empty;

        /// <summary>e.g. "24 h standby · 30 min alarm · ×1.25"</summary>
        public string StandardSummary =>
            $"{RequiredStandbyHours:F0} h standby · {RequiredAlarmMinutes:F0} min alarm · ×{SafetyFactor:F2}";
    }
}
