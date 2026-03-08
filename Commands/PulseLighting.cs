using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Runtime.InteropServices;

namespace Pulse.Commands
{
    /// <summary>
    /// External command that launches the main Pulse window in Lighting mode.
    /// Implements singleton window pattern — re-surfaces existing window if already open,
    /// switching to the Lighting module if a different module was active.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PulseLighting : IExternalCommand
    {
        /// <summary>Module ID that tells the shared Pulse window to activate the Lighting module.</summary>
        internal const string ModuleId = "Lighting";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Delegate to the shared window manager, requesting the Lighting module
                return PulseWindowManager.OpenOrFocus(commandData, ModuleId, ref message);
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
