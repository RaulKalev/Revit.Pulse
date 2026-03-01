using System;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Colour-coded status for a single capacity gauge.
    /// </summary>
    public enum CapacityStatus
    {
        Normal,   // below WarningFraction
        Warning,  // WarningFraction … CriticalFraction
        Critical  // above CriticalFraction
    }

    /// <summary>
    /// Addresses and mA usage data for a selected Panel or Loop.
    /// All computation lives here — the ViewModel only reads properties.
    /// </summary>
    public sealed class CapacityMetrics
    {
        public int    AddressesUsed { get; set; }
        public int    AddressesMax  { get; set; }
        public double MaUsed        { get; set; }
        public double MaMax         { get; set; }

        // ── Derived ──────────────────────────────────────────────────────────

        public double AddressFraction => AddressesMax > 0 ? (double)AddressesUsed / AddressesMax : 0;
        public double MaFraction      => MaMax > 0 ? MaUsed / MaMax : 0;

        public int    RemainingAddresses => Math.Max(0, AddressesMax - AddressesUsed);
        public double RemainingMa        => Math.Max(0, MaMax - MaUsed);

        /// <summary>Formatted "178 / 254 (70%)" string.</summary>
        public string AddressSummary =>
            AddressesMax > 0
                ? $"{AddressesUsed} / {AddressesMax} ({Math.Round(AddressFraction * 100)}%)"
                : "— / —";

        /// <summary>Formatted "410 / 500 mA (82%)" string.</summary>
        public string MaSummary =>
            MaMax > 0
                ? $"{Math.Round(MaUsed)} / {Math.Round(MaMax)} mA ({Math.Round(MaFraction * 100)}%)"
                : "— / — mA";

        public string RemainingAddressesSummary =>
            AddressesMax > 0 ? $"{RemainingAddresses} addresses remaining" : string.Empty;

        public string RemainingMaSummary =>
            MaMax > 0 ? $"{Math.Round(RemainingMa)} mA remaining" : string.Empty;

        public CapacityStatus AddressStatus => GetStatus(AddressFraction);
        public CapacityStatus MaStatus      => GetStatus(MaFraction);

        private static CapacityStatus GetStatus(double fraction)
        {
            if (fraction >= MetricsThresholds.CriticalFraction) return CapacityStatus.Critical;
            if (fraction >= MetricsThresholds.WarningFraction)  return CapacityStatus.Warning;
            return CapacityStatus.Normal;
        }
    }
}
