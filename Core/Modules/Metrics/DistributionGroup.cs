namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// One entry in the device distribution breakdown.
    /// Devices are grouped by DeviceType (user-configured parameter).
    /// </summary>
    public sealed class DistributionGroup
    {
        /// <summary>Device type label (e.g. "Smoke Detector").</summary>
        public string Name { get; set; }

        /// <summary>Number of devices of this type.</summary>
        public int Count { get; set; }

        /// <summary>
        /// Fraction of total devices this group represents (0–1).
        /// Used to render a proportional inline bar.
        /// </summary>
        public double Fraction { get; set; }
    }
}
