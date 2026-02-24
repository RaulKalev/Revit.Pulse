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
    /// Keys are stored as "LevelName|line", "LevelName|text-above", or "LevelName|text-below".
    /// Absence of a key means Visible.
    /// </summary>
    public class LevelVisibilitySettings
    {
        [JsonProperty("states")]
        public Dictionary<string, LevelState> States { get; set; } = new Dictionary<string, LevelState>();

        public LevelState GetLineState(string levelName)
            => States.TryGetValue(levelName + "|line", out var s) ? s : LevelState.Visible;

        /// <summary>Text label that sits above the level's own line.</summary>
        public LevelState GetTextAboveState(string levelName)
            => States.TryGetValue(levelName + "|text-above", out var s) ? s : LevelState.Visible;

        /// <summary>Text label that sits below the next higher level's line.</summary>
        public LevelState GetTextBelowState(string levelName)
            => States.TryGetValue(levelName + "|text-below", out var s) ? s : LevelState.Visible;

        public void SetLineState(string levelName, LevelState state)
            => SetKey(levelName + "|line", state);

        public void SetTextAboveState(string levelName, LevelState state)
            => SetKey(levelName + "|text-above", state);

        public void SetTextBelowState(string levelName, LevelState state)
            => SetKey(levelName + "|text-below", state);

        private void SetKey(string key, LevelState state)
        {
            if (state == LevelState.Visible)
                States.Remove(key);
            else
                States[key] = state;
        }
    }
}
