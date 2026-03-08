using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace Pulse.Commands
{
    /// <summary>
    /// External command that launches the main Pulse window in Fire Alarm mode.
    /// Delegates to <see cref="PulseWindowManager"/> for singleton window management.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PulseFireAlarm : IExternalCommand
    {
        /// <summary>Module ID that tells the shared Pulse window to activate the Fire Alarm module.</summary>
        internal const string ModuleId = "FireAlarm";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
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
