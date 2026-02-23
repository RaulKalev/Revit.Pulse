using System;
using System.Collections.Generic;
using System.Linq;
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
            SelectWithContext(elementIdValue, null);
        }

        /// <summary>
        /// Select <paramref name="primaryId"/> and zoom the active view to a bounding box
        /// that also includes any <paramref name="contextIds"/> that are visible in the
        /// same active view (e.g. neighbouring devices on the same loop).
        /// Only the primary element is added to the Revit selection set.
        /// </summary>
        public void SelectWithContext(long primaryId, IList<long> contextIds)
        {
            var doc   = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var primaryElemId = new ElementId(primaryId);
            var primaryElem   = doc.GetElement(primaryElemId);
            if (primaryElem == null) return;

            var uidoc = _uiApp.ActiveUIDocument;

            // Select only the primary element
            uidoc.Selection.SetElementIds(new[] { primaryElemId });

            // Build the zoom set: primary + context IDs that exist AND are
            // visible in the currently active view
            var zoomIds = new List<ElementId> { primaryElemId };

            if (contextIds != null && contextIds.Count > 0)
            {
                var activeView = uidoc.ActiveView;
                foreach (long ctxId in contextIds)
                {
                    if (ctxId == primaryId) continue;
                    var ctxElemId = new ElementId(ctxId);
                    var ctxElem   = doc.GetElement(ctxElemId);
                    if (ctxElem == null) continue;

                    // Check the element is visible in the active view
                    if (IsVisibleInView(doc, ctxElemId, activeView))
                        zoomIds.Add(ctxElemId);
                }
            }

            try
            {
                uidoc.ShowElements(zoomIds);
            }
            catch
            {
                // Fall back to showing only the primary element
                try { uidoc.ShowElements(primaryElemId); } catch { }
            }
        }

        /// <summary>
        /// Returns true if <paramref name="elemId"/> has a visible instance
        /// (non-null bounding box) in <paramref name="view"/>.
        /// </summary>
        private static bool IsVisibleInView(Document doc, ElementId elemId, View view)
        {
            try
            {
                var elem = doc.GetElement(elemId);
                if (elem == null) return false;
                var bb = elem.get_BoundingBox(view);
                return bb != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
