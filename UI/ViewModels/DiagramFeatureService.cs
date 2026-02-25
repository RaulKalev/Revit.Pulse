using System;
using System.Windows;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Modules.FireAlarm;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Orchestrates diagram-related feature logic that previously lived in MainViewModel:
    ///   - wiring assignment flow between diagram canvas and topology combobox
    ///   - visibility save callback wiring
    ///   - assignment save callback wiring
    ///
    /// The service holds NO Revit references — Revit writes are delegated through
    /// callbacks that MainViewModel wires to <c>StorageFacade</c>.
    /// </summary>
    public sealed class DiagramFeatureService
    {
        private readonly PulseAppController _appController;

        public DiagramFeatureService(PulseAppController appController)
        {
            _appController = appController ?? throw new ArgumentNullException(nameof(appController));
        }

        /// <summary>
        /// Handle a wire-assignment originating from the diagram canvas.
        /// Updates the topology combobox silently, locates the loop's devices,
        /// and invokes <paramref name="writeParameters"/> to push the value to Revit.
        /// </summary>
        public void OnDiagramWireAssigned(
            string panelName,
            string loopName,
            string wireName,
            TopologyViewModel topology,
            Action<string, string, string, Action<int>, Action<Exception>> writeParameters)
        {
            // Sync topology combobox immediately — independent of Revit write path
            var topoNode = topology?.FindLoopNode(panelName, loopName);
            topoNode?.SetAssignedWireSilent(wireName ?? string.Empty);

            var currentData = _appController.CurrentData;
            if (currentData == null) return;

            string paramName = _appController.ActiveSettings?.GetRevitParameterName(
                FireAlarmParameterKeys.Wire);
            if (string.IsNullOrEmpty(paramName)) return;

            // Find the loop by display name
            var loop = currentData.Loops.Find(l =>
                string.Equals(l.DisplayName, loopName, StringComparison.OrdinalIgnoreCase));
            if (loop == null) return;

            var elementIds = new System.Collections.Generic.List<long>();
            foreach (var d in loop.Devices)
                if (d.RevitElementId.HasValue)
                    elementIds.Add(d.RevitElementId.Value);

            if (elementIds.Count == 0) return;

            writeParameters?.Invoke(paramName, wireName ?? string.Empty, loopName, null, null);

            // actual write done by caller via lambda
        }
    }
}
