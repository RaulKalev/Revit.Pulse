using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Manages temporary graphic overrides for highlighting elements in Revit views.
    /// Uses OverrideGraphicSettings to visually distinguish selected or problematic elements.
    /// </summary>
    public class TemporaryOverrideService
    {
        private readonly UIApplication _uiApp;
        private readonly List<ElementId> _overriddenElements = new List<ElementId>();
        private View _overrideView;

        public TemporaryOverrideService(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        }

        /// <summary>
        /// Apply a color override to the specified elements in the active view.
        /// Must be called from the Revit API context (inside an ExternalEvent handler or transaction).
        /// </summary>
        public void HighlightElements(IEnumerable<long> elementIdValues, byte r, byte g, byte b)
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var view = _uiApp.ActiveUIDocument.ActiveView;
            if (view == null) return;

            var color = new Color(r, g, b);
            var settings = new OverrideGraphicSettings();
            settings.SetProjectionLineColor(color);
            settings.SetSurfaceForegroundPatternColor(color);
            settings.SetProjectionLineWeight(6);

            using (var tx = new Transaction(doc, "Pulse: Highlight Elements"))
            {
                tx.Start();

                foreach (long idValue in elementIdValues)
                {
                    var elementId = new ElementId(idValue);
                    if (doc.GetElement(elementId) != null)
                    {
                        view.SetElementOverrides(elementId, settings);
                        _overriddenElements.Add(elementId);
                    }
                }

                _overrideView = view;
                tx.Commit();
            }
        }

        /// <summary>
        /// Remove all temporary overrides previously applied by this service.
        /// Must be called from the Revit API context.
        /// </summary>
        public void ResetOverrides()
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null || _overrideView == null) return;

            var defaultSettings = new OverrideGraphicSettings();

            using (var tx = new Transaction(doc, "Pulse: Reset Overrides"))
            {
                tx.Start();

                foreach (var elementId in _overriddenElements)
                {
                    if (doc.GetElement(elementId) != null)
                    {
                        _overrideView.SetElementOverrides(elementId, defaultSettings);
                    }
                }

                tx.Commit();
            }

            _overriddenElements.Clear();
            _overrideView = null;
        }

        /// <summary>Whether there are currently active overrides.</summary>
        public bool HasActiveOverrides => _overriddenElements.Count > 0;
    }
}
