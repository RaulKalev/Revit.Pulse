using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Defines the visibility state of a single level line in the diagram panel.
    /// </summary>
    public enum LevelState
    {
        Visible,
        Hidden,
        Deleted
    }

    /// <summary>
    /// Persisted diagram display preferences: maps level name → visibility state.
    /// Serialised to Revit Extensible Storage so choices survive file close/reopen.
    /// </summary>
    public class LevelVisibilitySettings
    {
        [JsonProperty("states")]
        public Dictionary<string, LevelState> States { get; set; } = new Dictionary<string, LevelState>();

        /// <summary>Returns the current state for <paramref name="levelName"/>; defaults to Visible.</summary>
        public LevelState GetState(string levelName)
            => States.TryGetValue(levelName, out var s) ? s : LevelState.Visible;

        /// <summary>Sets the state for <paramref name="levelName"/>.</summary>
        public void SetState(string levelName, LevelState state)
        {
            if (state == LevelState.Visible)
                States.Remove(levelName);   // keep dict minimal — absence means Visible
            else
                States[levelName] = state;
        }
    }
}
