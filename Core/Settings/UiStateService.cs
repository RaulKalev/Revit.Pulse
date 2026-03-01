using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Lightweight UI state saved to %APPDATA%\Pulse\ui-state.json.
    /// Tracks which topology nodes were expanded so they can be restored on next launch.
    /// </summary>
    public class UiState
    {
        [JsonProperty("expandedNodeIds")]
        public HashSet<string> ExpandedNodeIds { get; set; } = new HashSet<string>();

        /// <summary>
        /// When true, the "Paste into AI agent" guidance popup is suppressed
        /// after clicking Run System Check.  Only a brief toast is shown instead.
        /// Persisted via UiStateService; respects existing JSON storage pattern.
        /// </summary>
        [JsonProperty("suppressAiPromptPopup")]
        public bool SuppressAiPromptPopup { get; set; }
    }

    /// <summary>
    /// Reads and writes <see cref="UiState"/> to a local JSON file.
    /// </summary>
    public static class UiStateService
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "ui-state.json");

        public static UiState Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    return JsonConvert.DeserializeObject<UiState>(json) ?? new UiState();
                }
            }
            catch { /* fall through */ }

            return new UiState();
        }

        public static void Save(UiState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonConvert.SerializeObject(state, Formatting.Indented));
        }
    }
}
