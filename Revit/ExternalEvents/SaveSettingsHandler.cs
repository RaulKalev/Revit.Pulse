using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Pulse.Core.Settings;
using Pulse.Revit.Storage;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that persists module settings to Revit Extensible Storage.
    /// Must run on the Revit API thread.
    /// </summary>
    public class SaveSettingsHandler : IExternalEventHandler
    {
        /// <summary>The settings to save. Set this before raising the event.</summary>
        public ModuleSettings Settings { get; set; }

        /// <summary>Callback invoked (on the calling thread) after a successful save.</summary>
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

                var service = new ExtensibleStorageService(doc);

                // Merge new settings into whatever is already stored
                var all = service.ReadSettings() ?? new Dictionary<string, ModuleSettings>();
                if (Settings != null)
                    all[Settings.ModuleId] = Settings;

                service.WriteSettings(all);
                OnSaved?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse: Save Settings";
    }
}
