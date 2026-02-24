using System.Collections.Generic;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Per-document topology assignments persisted to Revit Extensible Storage.
    /// These are project-specific — they describe how library items (panel configs,
    /// loop modules, wires) are assigned within a particular Revit model, and how
    /// the diagram is laid out for that model.
    ///
    /// Stored as a JSON blob in the <c>PulseTopologyAssignments</c> ES schema.
    /// </summary>
    public class TopologyAssignmentsStore
    {
        /// <summary>Panel label → assigned ControlPanelConfig name.</summary>
        public Dictionary<string, string> PanelAssignments { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Loop label → assigned LoopModuleConfig name.</summary>
        public Dictionary<string, string> LoopAssignments { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → true means draw wire on the right side of the panel.</summary>
        public Dictionary<string, bool> LoopFlipStates { get; set; }
            = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → number of extra horizontal lines added (total wires = 2 + value).</summary>
        public Dictionary<string, int> LoopExtraLines { get; set; }
            = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Level name → overridden elevation (Revit feet), set by Move mode in the diagram.</summary>
        public Dictionary<string, double> LevelElevationOffsets { get; set; }
            = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → assigned WireConfig name.</summary>
        public Dictionary<string, string> LoopWireAssignments { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>DeviceType string → symbol key. Each device type maps to one symbol for display.</summary>
        public Dictionary<string, string> SymbolMappings { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
