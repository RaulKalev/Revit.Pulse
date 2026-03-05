using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// "panelName::loopName" → visual rank within its side+elevation group (0 = bottommost).
        /// When absent the loop's natural index in LoopInfos is used as the default rank.
        /// </summary>
        public Dictionary<string, int> LoopRankOverrides { get; set; }
            = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>DeviceType string → symbol key. Each device type maps to one symbol for display.</summary>
        public Dictionary<string, string> SymbolMappings { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>"panelName::loopName" → true when 3-D wire routing model lines are shown for that loop.</summary>
        public Dictionary<string, bool> LoopWireRoutingVisible { get; set; }
            = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        // ── SubCircuit persistence (additive – safe to absent in older documents) ──────

        /// <summary>
        /// Opaque JSON blob for all SubCircuits.  Owned and serialised by
        /// <c>FireAlarmSubCircuitService</c> (Modules/FireAlarm) so that Core carries
        /// no dependency on the FA-specific SubCircuit type.
        /// Missing from old documents → null, treated as empty by the service.
        /// </summary>
        public string SubCircuitsJson { get; set; }

        // ── Migration shims — read old field names written by previous versions ──────

        /// <summary>
        /// Absorbs the old "SubCircuits" Dictionary field from documents saved before
        /// Gap-6.  When the blob slot is still empty the raw JSON token is stored as-is
        /// so <c>FireAlarmSubCircuitService</c> can deserialise it on next load.
        /// </summary>
        [JsonProperty("SubCircuits")]
        internal JToken LegacySubCircuitsToken
        {
            get => null;
            set
            {
                if (value != null && value.Type != JTokenType.Null
                    && string.IsNullOrEmpty(SubCircuitsJson))
                {
                    SubCircuitsJson = value.ToString(Formatting.None);
                }
            }
        }

        /// <summary>
        /// Absorbs the old "SubCircuitIdsByHostElementId" index field.
        /// The index is now rebuilt in-memory by <c>FireAlarmSubCircuitService</c>
        /// and is no longer persisted.
        /// </summary>
        [JsonProperty("SubCircuitIdsByHostElementId")]
        internal object LegacySubCircuitIndexToken
        {
            get => null;
            // ReSharper disable once ValueParameterNotUsed
            set { /* intentionally discarded — index is rebuilt on load */ }
        }
    }
}
