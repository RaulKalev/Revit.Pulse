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

        /// <summary>SubCircuit GUID string → assigned PsuConfig name (NAC PSU battery check).</summary>
        public Dictionary<string, string> SubCircuitPsuAssignments { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // ── Generic per-module blob store ────────────────────────────────────────────

        /// <summary>
        /// Per-module opaque JSON blobs.  Any module stores its data here under a
        /// module-specific key (e.g. <c>"FireAlarm.SubCircuits"</c>).  Core never
        /// inspects the values — modules own their own serialisation.
        ///
        /// Adding a new module requires no changes to this class: the module simply
        /// picks a unique key and reads/writes its blob through this dictionary.
        /// </summary>
        public Dictionary<string, string> ModuleBlobs { get; set; }
            = new Dictionary<string, string>(System.StringComparer.Ordinal);

        // ── Migration shims — read old field names written by previous versions ──────

        /// <summary>
        /// Absorbs the old "SubCircuits" Dictionary field from documents saved before
        /// Gap-6.  Copies the raw JSON token into <c>ModuleBlobs["FireAlarm.SubCircuits"]</c>
        /// so <c>FireAlarmSubCircuitService</c> can deserialise it on next load.
        /// </summary>
        [JsonProperty("SubCircuits")]
        internal JToken LegacySubCircuitsToken
        {
            get => null;
            set
            {
                if (value != null && value.Type != JTokenType.Null
                    && !ModuleBlobs.ContainsKey(FireAlarmSubCircuitsKey))
                {
                    ModuleBlobs[FireAlarmSubCircuitsKey] = value.ToString(Formatting.None);
                }
            }
        }

        /// <summary>
        /// Absorbs the old top-level <c>SubCircuitsJson</c> string field from documents
        /// saved between Gap-6 and Gap-2.  Migrates the value into
        /// <c>ModuleBlobs["FireAlarm.SubCircuits"]</c> on first load.
        /// </summary>
        [JsonProperty("SubCircuitsJson")]
        internal string LegacySubCircuitsJsonBlob
        {
            get => null;
            set
            {
                if (!string.IsNullOrEmpty(value)
                    && !ModuleBlobs.ContainsKey(FireAlarmSubCircuitsKey))
                {
                    ModuleBlobs[FireAlarmSubCircuitsKey] = value;
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

        // ── Internal constants shared with module services ────────────────────────────

        /// <summary>
        /// The key used by <c>FireAlarmSubCircuitService</c> inside <see cref="ModuleBlobs"/>.
        /// Declared here so Core can set up migration shims without taking a direct
        /// dependency on the FireAlarm module assembly.
        /// </summary>
        internal const string FireAlarmSubCircuitsKey = "FireAlarm.SubCircuits";
    }
}
