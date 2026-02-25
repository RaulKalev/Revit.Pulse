using System;
using Autodesk.Revit.UI;
using Pulse.Core.Settings;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that persists module settings to the local JSON store.
    /// Parameter mappings are stored in %APPDATA%\Pulse\device-config.json and are
    /// machine-wide — they are NOT stored in Revit Extensible Storage.
    /// </summary>
    public class SaveSettingsHandler : IExternalEventHandler
    {
        /// <summary>The settings to save. Set this before raising the event.</summary>
        public ModuleSettings Settings { get; set; }

        /// <summary>Callback invoked after a successful save.</summary>
        public Action OnSaved { get; set; }

        /// <summary>Callback invoked if the save fails.</summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                if (Settings == null)
                {
                    OnError?.Invoke(new InvalidOperationException("Settings is null."));
                    return;
                }

                var jsonStore = DeviceConfigService.Load();
                jsonStore.ModuleSettings[Settings.ModuleId] = Settings;
                DeviceConfigService.Save(jsonStore);

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
