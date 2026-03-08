using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Pulse.Modules.Lighting;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that assigns the current Revit selection to a DALI line.
    /// Writes two instance parameters (Line + Controller) to every selected element.
    /// </summary>
    public class AssignToLineHandler : IExternalEventHandler
    {
        /// <summary>Name of the Revit parameter that stores the line identifier (e.g. "Dali siin").</summary>
        public string LineParamName { get; set; }

        /// <summary>Value to write to <see cref="LineParamName"/> (e.g. "1").</summary>
        public string LineName { get; set; }

        /// <summary>Name of the Revit parameter that stores the controller name (e.g. "Dali kontroller").</summary>
        public string ControllerParamName { get; set; }

        /// <summary>Value to write to <see cref="ControllerParamName"/> (e.g. "DALI-1").</summary>
        public string ControllerName { get; set; }

        /// <summary>Invoked on the UI thread after the write completes (success or partial).</summary>
        public Action<AssignLineFeedback> OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            var feedback = new AssignLineFeedback();
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null)
                {
                    feedback.Success = false;
                    feedback.Message = "No active Revit document.";
                    Dispatch(feedback);
                    return;
                }

                if (string.IsNullOrEmpty(LineParamName) || string.IsNullOrEmpty(ControllerParamName))
                {
                    feedback.Success = false;
                    feedback.Message = "Line or Controller parameter name is not configured. Check Settings → Parameter Mapping.";
                    Dispatch(feedback);
                    return;
                }

                var selIds = uidoc.Selection.GetElementIds();
                if (selIds == null || selIds.Count == 0)
                {
                    feedback.Success = false;
                    feedback.Message = "No elements selected. Select DALI fixture elements in Revit first.";
                    Dispatch(feedback);
                    return;
                }

                int assigned = 0;
                int skipped = 0;

                using (var tx = new Transaction(doc, "Pulse: Assign to Lighting Line"))
                {
                    tx.Start();

                    foreach (var eid in selIds)
                    {
                        var elem = doc.GetElement(eid);
                        if (elem == null) { skipped++; continue; }

                        bool lineOk = TrySetParam(elem, LineParamName, LineName);
                        bool ctrlOk = TrySetParam(elem, ControllerParamName, ControllerName);

                        if (lineOk && ctrlOk)
                            assigned++;
                        else
                            skipped++;
                    }

                    tx.Commit();
                }

                feedback.Success = true;
                feedback.AssignedCount = assigned;
                feedback.SkippedCount = skipped;
                feedback.Message = skipped == 0
                    ? $"Assigned {assigned} element(s) to {ControllerName} / {LineName}."
                    : $"Assigned {assigned} element(s); {skipped} skipped (missing parameters).";
            }
            catch (Exception ex)
            {
                feedback.Success = false;
                feedback.Message = $"Assign failed: {ex.Message}";
            }

            Dispatch(feedback);
        }

        private void Dispatch(AssignLineFeedback feedback)
        {
            if (Application.Current?.Dispatcher != null)
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    (Action)(() => OnComplete?.Invoke(feedback)));
            else
                OnComplete?.Invoke(feedback);
        }

        private static bool TrySetParam(Element elem, string paramName, string value)
        {
            var p = elem.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;

            if (p.StorageType == StorageType.String)
                return p.Set(value);

            // Numeric: try to parse
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                if (p.StorageType == StorageType.Double)
                    return p.Set(d);
                if (p.StorageType == StorageType.Integer)
                    return p.Set((int)d);
            }

            return false;
        }

        public string GetName() => "Pulse: Assign to Lighting Line";
    }
}
