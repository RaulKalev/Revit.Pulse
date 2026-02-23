using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Provides Revit element selection from the UI layer.
    /// All operations use ExternalEvent to ensure they run on the Revit API thread.
    /// </summary>
    public class SelectionService
    {
        private readonly UIApplication _uiApp;

        public SelectionService(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        }

        /// <summary>
        /// Select an element in Revit by its ElementId value.
        /// Must be called from the Revit API context (inside an ExternalEvent handler).
        /// </summary>
        public void SelectElement(long elementIdValue)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var elementId = new ElementId(elementIdValue);
            var element = doc.GetElement(elementId);
            if (element == null) return;

            var uidoc = _uiApp.ActiveUIDocument;
            uidoc.Selection.SetElementIds(new[] { elementId });

            // Zoom to the element
            try
            {
                uidoc.ShowElements(elementId);
            }
            catch
            {
                // ShowElements may fail for certain element types — non-critical
            }
        }
    }
}
