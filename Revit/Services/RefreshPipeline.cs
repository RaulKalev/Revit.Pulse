using System;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Revit.ExternalEvents;
using Pulse.Revit.Storage;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Encapsulates the Refresh → Collect → Build → Evaluate pipeline.
    /// Owns the CollectDevicesHandler + ExternalEvent pair and provides a
    /// clean invocation API. Extracted from MainViewModel to decouple the
    /// refresh lifecycle from the UI layer.
    ///
    /// All Revit API work runs on the Revit thread via ExternalEvent;
    /// callbacks are marshalled by the caller (MainViewModel dispatches to UI).
    /// </summary>
    public class RefreshPipeline
    {
        private readonly CollectDevicesHandler _handler;
        private readonly ExternalEvent _event;

        /// <summary>
        /// Topology assignments read during the last refresh (on the Revit thread).
        /// May be null if no refresh has occurred yet.
        /// </summary>
        public TopologyAssignmentsStore RefreshedAssignments => _handler.RefreshedAssignments;

        public RefreshPipeline()
        {
            _handler = new CollectDevicesHandler();
            _event = ExternalEvent.Create(_handler);
        }

        /// <summary>
        /// Kick off a new refresh cycle.
        /// </summary>
        /// <param name="module">Active module definition.</param>
        /// <param name="settings">Current module settings.</param>
        /// <param name="onCompleted">Called on success (may be on Revit thread).</param>
        /// <param name="onError">Called on failure (may be on Revit thread).</param>
        public void Execute(
            IModuleDefinition module,
            ModuleSettings settings,
            Action<ModuleData> onCompleted,
            Action<Exception> onError)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));

            _handler.Collector = module.CreateCollector();
            _handler.TopologyBuilder = module.CreateTopologyBuilder();
            _handler.RulePack = module.CreateRulePack();
            _handler.Settings = settings;
            _handler.OnCompleted = onCompleted;
            _handler.OnError = onError;

            _event.Raise();
        }
    }
}
