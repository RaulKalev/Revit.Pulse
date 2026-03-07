namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Centralised thresholds for capacity warnings and critical levels.
    /// Change values here to re-tune colour coding across the entire Metrics panel.
    /// </summary>
    public static class MetricsThresholds
    {
        /// <summary>Fraction at which an address / mA gauge turns amber (70%).</summary>
        public const double WarningFraction  = 0.70;

        /// <summary>Fraction at which an address / mA gauge turns red (85%).</summary>
        public const double CriticalFraction = 0.85;

        /// <summary>
        /// Minimum spread between the most- and least-loaded loop (by address utilisation)
        /// before a loop-imbalance warning is raised.  A spread below this value is
        /// considered acceptably balanced.
        /// </summary>
        public const double LoopImbalanceSpreadThreshold = 0.40;
    }
}
