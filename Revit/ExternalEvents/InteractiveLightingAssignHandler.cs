using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Pulse.Modules.Lighting;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that runs an interactive Revit pick session and then
    /// writes Line + Controller instance parameters to every picked element.
    /// </summary>
    public class InteractiveLightingAssignHandler : IExternalEventHandler
    {
        /// <summary>Name of the Revit parameter that stores the line identifier.</summary>
        public string LineParamName { get; set; }

        /// <summary>Value to write to <see cref="LineParamName"/>.</summary>
        public string LineName { get; set; }

        /// <summary>Name of the Revit parameter that stores the controller name.</summary>
        public string ControllerParamName { get; set; }

        /// <summary>Value to write to <see cref="ControllerParamName"/>.</summary>
        public string ControllerName { get; set; }

        /// <summary>Status bar message shown while picking.</summary>
        public string PromptMessage { get; set; } = "Select lighting fixtures to assign to line (Finish / Escape to confirm)";

        /// <summary>Invoked on the UI thread after the operation completes (success, cancel, or error).</summary>
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
                    feedback.Message = "No active Revit document.";
                    Dispatch(feedback);
                    return;
                }

                if (string.IsNullOrEmpty(LineParamName) || string.IsNullOrEmpty(ControllerParamName))
                {
                    feedback.Message = "Line or Controller parameter name is not configured. Check Settings → Parameter Mapping.";
                    Dispatch(feedback);
                    return;
                }

                // Block until the user finishes (double-click / Finish) or presses Escape
                IList<Reference> refs;
                try
                {
                    refs = uidoc.Selection.PickObjects(ObjectType.Element, PromptMessage);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    feedback.Success = false;
                    feedback.Message = "Pick cancelled.";
                    Dispatch(feedback);
                    return;
                }

                if (refs == null || refs.Count == 0)
                {
                    feedback.Success = false;
                    feedback.Message = "No elements picked.";
                    Dispatch(feedback);
                    return;
                }

                int assigned = 0;
                int skipped = 0;

                using (var tx = new Transaction(doc, "Pulse: Interactive Assign to Lighting Line"))
                {
                    tx.Start();

                    foreach (var r in refs)
                    {
                        var elem = doc.GetElement(r.ElementId);
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
                feedback.Message = $"Interactive assign failed: {ex.Message}";
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

        public string GetName() => "Pulse: Interactive Assign to Lighting Line";
    }
}
