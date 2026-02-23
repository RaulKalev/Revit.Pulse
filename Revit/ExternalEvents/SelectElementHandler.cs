using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that selects an element in Revit and zooms to it
    /// together with optional neighbour elements (e.g. adjacent loop devices).
    /// </summary>
    public class SelectElementHandler : IExternalEventHandler
    {
        /// <summary>The primary ElementId to select.</summary>
        public long ElementIdToSelect { get; set; }

        /// <summary>
        /// Additional ElementIds to include in the zoom bounding box.
        /// Only elements visible in the active view are included.
        /// The primary element is always zoomed to regardless.
        /// </summary>
        public IList<long> ContextIds { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var service = new Services.SelectionService(app);
                service.SelectWithContext(ElementIdToSelect, ContextIds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] SelectElementHandler error: {ex.Message}");
            }
        }

        public string GetName() => "Pulse.SelectElement";
    }
}
