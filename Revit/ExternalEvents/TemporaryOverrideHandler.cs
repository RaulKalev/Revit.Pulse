using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that applies temporary graphic overrides to highlight elements.
    /// </summary>
    public class TemporaryOverrideHandler : IExternalEventHandler
    {
        /// <summary>ElementId values to highlight. Set before raising the event.</summary>
        public List<long> ElementIds { get; set; } = new List<long>();

        /// <summary>Override color (R, G, B). Defaults to orange.</summary>
        public byte ColorR { get; set; } = 255;
        public byte ColorG { get; set; } = 140;
        public byte ColorB { get; set; } = 0;

        /// <summary>
        /// Reference to the override service, so the same instance can be used for reset.
        /// </summary>
        public Services.TemporaryOverrideService OverrideService { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                if (OverrideService == null)
                {
                    OverrideService = new Services.TemporaryOverrideService(app);
                }

                OverrideService.HighlightElements(ElementIds, ColorR, ColorG, ColorB);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] TemporaryOverrideHandler error: {ex.Message}");
            }
        }

        public string GetName() => "Pulse.TemporaryOverride";
    }
}
