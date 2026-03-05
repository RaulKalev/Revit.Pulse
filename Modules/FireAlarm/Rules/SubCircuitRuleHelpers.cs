namespace Pulse.Modules.FireAlarm.Rules
{
    /// <summary>
    /// Shared utilities for SubCircuit-aware validation rules.
    /// </summary>
    internal static class SubCircuitRuleHelpers
    {
        private static readonly string[] _sounderKeywords =
        {
            "sounder", "nac", "notification", "horn", "bell", "siren",
            "speaker", "strobe", "annunciator", "buzzer"
        };

        /// <summary>
        /// Returns true when the device type string indicates a notification appliance
        /// (sounder, strobe, horn, etc.) that lives on a NAC/SubCircuit rather than
        /// on an addressable loop.  These devices do not require Panel / Loop / Address
        /// parameters — they are validated by the SubCircuit rules instead.
        /// </summary>
        public static bool IsSounderType(string deviceType)
        {
            if (string.IsNullOrWhiteSpace(deviceType)) return false;
            string lower = deviceType.ToLowerInvariant();
            foreach (string keyword in _sounderKeywords)
                if (lower.Contains(keyword)) return true;
            return false;
        }
    }
}
