using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that highlights all devices assigned to a given DALI line
    /// in the active view using a temporary graphic override with the line's color.
    /// </summary>
    public class HighlightLightingLineHandler : IExternalEventHandler
    {
        /// <summary>Name of the Revit parameter that stores the line identifier on devices.</summary>
        public string LineParamName { get; set; }

        /// <summary>Line value to match (e.g. "1").</summary>
        public string LineName { get; set; }

        /// <summary>Name of the Revit parameter that stores the controller name on devices.</summary>
        public string ControllerParamName { get; set; }

        /// <summary>Controller value to match (e.g. "DALI-1").</summary>
        public string ControllerName { get; set; }

        /// <summary>
        /// ARGB or RGB hex color string for the highlight (e.g. "#FFFF4081" or "#FF4081").
        /// </summary>
        public string ColorHex { get; set; } = "#FFFF4081";

        /// <summary>
        /// Reference to the shared override service so previous highlights can be cleared.
        /// If null a new instance is created.
        /// </summary>
        public Services.TemporaryOverrideService OverrideService { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return;

                if (OverrideService == null)
                    OverrideService = new Services.TemporaryOverrideService(app);

                // Reset previous highlights first
                OverrideService.ResetOverrides();

                if (string.IsNullOrEmpty(LineParamName) || string.IsNullOrEmpty(ControllerParamName))
                    return;

                // Collect all elements in the active view that match line + controller
                var matchingIds = new List<long>();
                var activeView = uidoc.ActiveView;
                if (activeView == null) return;

                var collector = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    string lineVal = GetParamString(elem, LineParamName);
                    string ctrlVal = GetParamString(elem, ControllerParamName);
                    if (string.Equals(lineVal, LineName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(ctrlVal, ControllerName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (elem.Id != null)
                            matchingIds.Add(elem.Id.Value);
                    }
                }

                if (matchingIds.Count == 0) return;

                // Parse hex color (#AARRGGBB or #RRGGBB)
                ParseHexColor(ColorHex, out byte r, out byte g, out byte b);
                OverrideService.HighlightElements(matchingIds, r, g, b);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] HighlightLightingLineHandler error: {ex.Message}");
            }
        }

        public string GetName() => "Pulse: Highlight Lighting Line";

        private static string GetParamString(Element elem, string paramName)
        {
            var p = elem.LookupParameter(paramName);
            if (p == null) return null;
            return p.StorageType == StorageType.String
                ? p.AsString()
                : p.AsValueString();
        }

        private static void ParseHexColor(string hex, out byte r, out byte g, out byte b)
        {
            r = 255; g = 64; b = 129; // default pink/magenta
            if (string.IsNullOrEmpty(hex)) return;
            hex = hex.TrimStart('#');
            try
            {
                if (hex.Length == 8) // AARRGGBB
                {
                    r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                }
                else if (hex.Length == 6) // RRGGBB
                {
                    r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                }
            }
            catch { /* keep defaults */ }
        }
    }
}
