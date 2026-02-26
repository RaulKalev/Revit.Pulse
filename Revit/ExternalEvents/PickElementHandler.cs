using System;
using System.Windows;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that lets the user pick a single element in Revit.
    /// The pick blocks the Revit API thread until the user clicks (or cancels).
    /// The plugin window should be minimized before raising this event and restored
    /// in the <see cref="OnPicked"/> / <see cref="OnCancelled"/> callbacks.
    /// </summary>
    public class PickElementHandler : IExternalEventHandler
    {
        /// <summary>Status message shown in the Revit status bar while picking.</summary>
        public string PromptMessage { get; set; } = "Select an element to assign";

        /// <summary>Invoked (on the Revit API thread) when the user picks an element.
        /// Use <c>Dispatcher.BeginInvoke</c> to marshal back to the UI thread.</summary>
        public Action<long> OnPicked { get; set; }

        /// <summary>Invoked when the user presses Escape / cancels the pick.</summary>
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

                // PickObject blocks until the user selects or presses Escape
                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    PromptMessage);

                if (reference == null)
                {
                    OnCancelled?.Invoke();
                    return;
                }

                long elementId = reference.ElementId.Value;
                OnPicked?.Invoke(elementId);
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

        public string GetName() => "Pulse: Pick Element";
    }
}
