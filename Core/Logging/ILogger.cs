using System;

namespace Pulse.Core.Logging
{
    /// <summary>
    /// Simple logging abstraction for the Pulse platform.
    /// Implementations can write to Revit journal, file, or debug output.
    /// </summary>
    public interface ILogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception ex = null);
    }

    /// <summary>
    /// Default logger that writes to System.Diagnostics.Debug.
    /// Used when no other logger is configured.
    /// </summary>
    public class DebugLogger : ILogger
    {
        private readonly string _prefix;

        public DebugLogger(string prefix = "Pulse")
        {
            _prefix = prefix;
        }

        public void Info(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{_prefix}] INFO: {message}");
        }

        public void Warning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{_prefix}] WARN: {message}");
        }

        public void Error(string message, Exception ex = null)
        {
            System.Diagnostics.Debug.WriteLine($"[{_prefix}] ERROR: {message}");
            if (ex != null)
            {
                System.Diagnostics.Debug.WriteLine($"[{_prefix}] EXCEPTION: {ex}");
            }
        }
    }
}
