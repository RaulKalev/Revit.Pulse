namespace Pulse.Core.Rules
{
    /// <summary>
    /// Severity level for rule validation results.
    /// </summary>
    public enum Severity
    {
        /// <summary>Informational finding — no action required.</summary>
        Info,

        /// <summary>Potential issue that should be reviewed.</summary>
        Warning,

        /// <summary>Critical problem that must be resolved.</summary>
        Error
    }
}
