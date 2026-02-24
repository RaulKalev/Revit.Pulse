using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pulse.Core.Modules;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Diagram panel.
    /// Holds the list of project levels used to draw the background level grid.
    /// Additional diagram data (devices, loops, etc.) will be added here later.
    /// </summary>
    public class DiagramViewModel : ViewModelBase
    {
        /// <summary>Project levels ordered by elevation ascending.</summary>
        public ObservableCollection<LevelInfo> Levels { get; } = new ObservableCollection<LevelInfo>();

        /// <summary>Replace the levels collection with fresh data from the last Refresh.</summary>
        public void LoadLevels(IEnumerable<LevelInfo> levels)
        {
            Levels.Clear();
            foreach (var level in levels)
                Levels.Add(level);
        }
    }
}
