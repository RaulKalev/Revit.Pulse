using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that lets the user pick multiple elements in Revit.
    /// Uses <c>PickObjects</c> which runs until the user finishes the multi-select
    /// session (double-click / Finish / Escape).
    /// The plugin window should be minimised before raising this event and restored
    /// in the callbacks.
    /// </summary>
    public class PickMultipleElementsHandler : IExternalEventHandler
    {
        /// <summary>Status message shown in the Revit status bar while picking.</summary>
        public string PromptMessage { get; set; } = "Select elements (finish to confirm)";

        /// <summary>Invoked (on the Revit API thread) with the list of picked element IDs.
        /// Use <c>Dispatcher.BeginInvoke</c> to marshal back to the UI thread.</summary>
        public Action<List<long>> OnPicked { get; set; }

        /// <summary>Invoked when the user presses Escape / cancels without selecting anything.</summary>
        public Action OnCancelled { get; set; }

        /// <summary>Invoked on unexpected errors.</summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    OnError?.Invoke(new InvalidOperationException("No active document."));
                    return;
                }

                // PickObjects blocks until the user finishes the selection or presses Escape
                IList<Reference> references = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    PromptMessage);

                if (references == null || references.Count == 0)
                {
                    OnCancelled?.Invoke();
                    return;
                }

                var ids = new List<long>(references.Count);
                foreach (var r in references)
                    ids.Add(r.ElementId.Value);

                OnPicked?.Invoke(ids);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User pressed Escape — not an error
                OnCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse: Pick Multiple Elements";
    }
}
