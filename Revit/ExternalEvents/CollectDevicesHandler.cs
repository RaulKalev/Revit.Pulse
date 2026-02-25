using System;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that collects device data from the active Revit document.
    /// This is the main data refresh mechanism — triggered by the Refresh button in the UI.
    /// </summary>
    public class CollectDevicesHandler : IExternalEventHandler
    {
        /// <summary>The module collector to use.</summary>
        public IModuleCollector Collector { get; set; }

        /// <summary>The module settings (categories + parameter mapping).</summary>
        public ModuleSettings Settings { get; set; }

        /// <summary>The topology builder to use after collection.</summary>
        public ITopologyBuilder TopologyBuilder { get; set; }

        /// <summary>The rule pack to evaluate after building topology.</summary>
        public IRulePack RulePack { get; set; }

        /// <summary>
        /// Callback invoked on the UI thread after collection completes.
        /// Receives the populated ModuleData.
        /// </summary>
        public Action<ModuleData> OnCompleted { get; set; }

        /// <summary>
        /// Callback invoked if collection fails.
        /// </summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    OnError?.Invoke(new InvalidOperationException("No active document."));
                    return;
                }

                // Create the Revit collector context
                var context = new Services.RevitCollectorService(doc);

                // Run the module collector
                ModuleData data = Collector.Collect(context, Settings);

                // Build topology graph
                TopologyBuilder?.Build(data);

                // Run rules
                RulePack?.Evaluate(data);

                // Collect project levels for the diagram
                var levels = context.GetLevels();
                data.Levels.AddRange(levels);

                // Return results to the UI
                OnCompleted?.Invoke(data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] CollectDevicesHandler error: {ex.Message}");
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse.CollectDevices";
    }
}
