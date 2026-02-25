using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// User-editable labels and counts for a single fire-alarm panel symbol on the diagram canvas.
    /// Keyed by panel name inside <see cref="DiagramCanvasSettings.PanelSettings"/>.
    /// </summary>
    public class PanelDiagramSettings
    {
        /// <summary>
        /// Subtitle shown inside the panel symbol (the "Tulekahjusignalisatsiooni…" line).
        /// Supports two display lines separated by '\n'.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "Tulekahjusignalisatsiooni keskseade\nAnaloogadresseeritav";

        /// <summary>Number of output cells drawn below the panel symbol.</summary>
        [JsonProperty("outCount")]
        public int OutCount { get; set; } = 5;

        /// <summary>Supply label drawn at the bottom-right corner of the panel symbol.</summary>
        [JsonProperty("supply")]
        public string Supply { get; set; } = "Toide 230V";

        /// <summary>
        /// Per-output custom labels. Index 0 = Out1, etc.
        /// Shorter than OutCount → remaining outputs fall back to "Out{n}".
        /// </summary>
        [JsonProperty("outputLabels")]
        public List<string> OutputLabels { get; set; } = new List<string>();
    }

    /// <summary>
    /// Per-machine visual settings for the diagram canvas panel.
    /// Saved to %APPDATA%\Pulse\diagram-canvas-settings.json.
    /// </summary>
    public class DiagramCanvasSettings
    {
        /// <summary>
        /// Vertical distance (in canvas pixels) between adjacent wire rows of the same loop.
        /// Default: 13.0 px.
        /// </summary>
        [JsonProperty("wireSpacingPx")]
        public double WireSpacingPx { get; set; } = 13.0;

        /// <summary>
        /// Fixed horizontal distance (in canvas pixels) between consecutive device slots
        /// on a loop wire.  0 means automatic (devices distributed evenly along the wire span).
        /// Default: 0 (automatic).
        /// </summary>
        [JsonProperty("deviceSpacingPx")]
        public double DeviceSpacingPx { get; set; } = 0.0;

        /// <summary>
        /// When true, runs of 4+ consecutive same-type devices on a wire row are collapsed
        /// to first … last with the middle replaced by a ··· marker and a gap in the wire line.
        /// Default: false.
        /// </summary>
        [JsonProperty("showRepetitions")]
        public bool ShowRepetitions { get; set; } = false;

        /// <summary>
        /// When true, each device slot displays a small rotated address label above it.
        /// Default: false.
        /// </summary>
        [JsonProperty("showAddressLabels")]
        public bool ShowAddressLabels { get; set; } = false;

        /// <summary>
        /// Vertical distance in canvas pixels between the wire centre and the
        /// near edge of the address label. Default: 10 px.
        /// </summary>
        [JsonProperty("labelOffsetPx")]
        public double LabelOffsetPx { get; set; } = 10.0;

        /// <summary>
        /// Per-panel overrides for subtitle, output count and supply label.
        /// Keyed by panel name.
        /// </summary>
        [JsonProperty("panelSettings")]
        public Dictionary<string, PanelDiagramSettings> PanelSettings { get; set; }
            = new Dictionary<string, PanelDiagramSettings>();
    }

    /// <summary>
    /// Reads and writes <see cref="DiagramCanvasSettings"/> to a local JSON file.
    /// </summary>
    public static class DiagramCanvasSettingsService
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "diagram-canvas-settings.json");

        public static DiagramCanvasSettings Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    return JsonConvert.DeserializeObject<DiagramCanvasSettings>(json)
                           ?? new DiagramCanvasSettings();
                }
            }
            catch { /* fall through */ }
            return new DiagramCanvasSettings();
        }

        public static void Save(DiagramCanvasSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
