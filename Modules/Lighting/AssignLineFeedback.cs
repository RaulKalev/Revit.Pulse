namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Result model returned after an "Assign to Line" write-back operation.
    /// </summary>
    public sealed class AssignLineFeedback
    {
        public bool Success { get; set; }
        public int AssignedCount { get; set; }
        public int SkippedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
