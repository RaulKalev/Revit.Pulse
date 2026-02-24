using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Defines the visibility state of a single level element (line or text label).
    /// </summary>
    public enum LevelState
    {
        Visible,
        Hidden,
        Deleted
    }

    /// <summary>
    /// Persisted diagram display preferences.
    /// Keys are stored as "LevelName|line" or "LevelName|text".
    /// Absence of a key means Visible.
    /// </summary>
    public class LevelVisibilitySettings
    {
        [JsonProperty("states")]
        public Dictionary<string, LevelState> States { get; set; } = new Dictionary<string, LevelState>();

        public LevelState GetLineState(string levelName)
            => States.TryGetValue(levelName + "|line", out var s) ? s : LevelState.Visible;

        public LevelState GetTextState(string levelName)
            => States.TryGetValue(levelName + "|text", out var s) ? s : LevelState.Visible;

        public void SetLineState(string levelName, LevelState state)
            => SetKey(levelName + "|line", state);

        public void SetTextState(string levelName, LevelState state)
            => SetKey(levelName + "|text", state);

        private void SetKey(string key, LevelState state)
        {
            if (state == LevelState.Visible)
                States.Remove(key);
            else
                States[key] = state;
        }
    }
}
