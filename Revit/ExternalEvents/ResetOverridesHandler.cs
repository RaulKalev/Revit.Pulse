using System;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that removes all temporary graphic overrides applied by Pulse.
    /// </summary>
    public class ResetOverridesHandler : IExternalEventHandler
    {
        /// <summary>
        /// Reference to the override service that holds the active overrides.
        /// Must be the same instance used by TemporaryOverrideHandler.
        /// </summary>
        public Services.TemporaryOverrideService OverrideService { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                OverrideService?.ResetOverrides();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] ResetOverridesHandler error: {ex.Message}");
            }
        }

        public string GetName() => "Pulse.ResetOverrides";
    }
}
