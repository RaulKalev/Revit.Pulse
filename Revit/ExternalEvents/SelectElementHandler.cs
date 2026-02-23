using System;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that selects an element in Revit and optionally zooms to it.
    /// Used when the user clicks a device node in the Pulse topology view.
    /// </summary>
    public class SelectElementHandler : IExternalEventHandler
    {
        /// <summary>The ElementId value to select. Set before raising the event.</summary>
        public long ElementIdToSelect { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var service = new Services.SelectionService(app);
                service.SelectElement(ElementIdToSelect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] SelectElementHandler error: {ex.Message}");
            }
        }

        public string GetName() => "Pulse.SelectElement";
    }
}
