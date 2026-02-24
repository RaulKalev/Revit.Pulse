using System;
using Autodesk.Revit.UI;
using Pulse.Core.Settings;
using Pulse.Revit.Storage;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that persists topology assignments
    /// (panel/loop config assignments, loop flip states, extra lines,
    /// level elevation offsets, and wire assignments) to Revit Extensible Storage.
    /// Must run on the Revit API thread.
    /// </summary>
    public class SaveTopologyAssignmentsHandler : IExternalEventHandler
    {
        /// <summary>The assignments to save. Set this before raising the event.</summary>
        public TopologyAssignmentsStore Store { get; set; }

        /// <summary>Callback invoked after a successful save.</summary>
        public Action OnSaved { get; set; }

        /// <summary>Callback invoked if the save fails.</summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    OnError?.Invoke(new InvalidOperationException("No active Revit document."));
                    return;
                }

                if (Store == null) return;

                var service = new ExtensibleStorageService(doc);
                service.WriteTopologyAssignments(Store);
                OnSaved?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse: Save Topology Assignments";
    }
}
