using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pulse.Core.Modules;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Diagram panel.
    /// Holds the list of project levels used to draw the background level grid,
    /// and manages per-level visibility preferences that are persisted to Extensible Storage.
    /// </summary>
    public class DiagramViewModel : ViewModelBase
    {
        // ── Levels ────────────────────────────────────────────────────────

        /// <summary>Project levels ordered by elevation ascending.</summary>
        public ObservableCollection<LevelInfo> Levels { get; } = new ObservableCollection<LevelInfo>();

        /// <summary>Replace the levels collection with fresh data from the last Refresh.</summary>
        public void LoadLevels(IEnumerable<LevelInfo> levels)
        {
            Levels.Clear();
            foreach (var level in levels)
                Levels.Add(level);
        }

        // ── Visibility preferences ─────────────────────────────────────────

        private LevelVisibilitySettings _visibility = new LevelVisibilitySettings();

        /// <summary>Current visibility settings (read-only snapshot for storage).</summary>
        public LevelVisibilitySettings Visibility => _visibility;

        /// <summary>
        /// Delegate raised whenever the user changes a level's visibility state.
        /// MainViewModel wires this to raise the SaveDiagramSettings ExternalEvent.
        /// </summary>
        public Action VisibilityChanged { get; set; }

        /// <summary>Restore persisted visibility settings loaded from Extensible Storage.</summary>
        public void LoadVisibility(LevelVisibilitySettings settings)
        {
            _visibility = settings ?? new LevelVisibilitySettings();
            OnPropertyChanged(nameof(Visibility));
        }

        /// <summary>Returns the current display state for a level by name.</summary>
        public LevelState GetLevelState(string levelName)
            => _visibility.GetState(levelName);

        /// <summary>
        /// Change the display state for a level and request a save.
        /// </summary>
        public void SetLevelState(string levelName, LevelState state)
        {
            _visibility.SetState(levelName, state);
            OnPropertyChanged(nameof(Visibility));
            VisibilityChanged?.Invoke();
        }
    }
}

