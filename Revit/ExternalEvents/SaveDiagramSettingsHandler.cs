using System;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Revit.Storage;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that persists diagram display preferences
    /// (level line visibility states) to Revit Extensible Storage.
    /// Must run on the Revit API thread.
    /// </summary>
    public class SaveDiagramSettingsHandler : IExternalEventHandler
    {
        /// <summary>The settings to save. Set this before raising the event.</summary>
        public LevelVisibilitySettings Settings { get; set; }

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

                if (Settings == null) return;

                var service = new ExtensibleStorageService(doc);
                service.WriteDiagramSettings(Settings);
                OnSaved?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse: Save Diagram Settings";
    }
}
