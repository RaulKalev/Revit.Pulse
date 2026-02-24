using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.UI.ViewModels
{
    public readonly struct PanelInfo
    {
        public string Name             { get; }
        public double? Elevation        { get; }
        /// <summary>Actual loop names extracted from the model for this panel (sorted).</summary>
        public IReadOnlyList<string> LoopNames { get; }
        /// <summary>MaxLoopCount from the assigned ControlPanelConfig (0 = no config assigned).</summary>
        public int ConfigLoopCount { get; }
        public PanelInfo(string name, double? elevation,
                         IReadOnlyList<string> loopNames, int configLoopCount)
        { Name = name; Elevation = elevation; LoopNames = loopNames; ConfigLoopCount = configLoopCount; }
    }

    public readonly struct NonVisibleItem
    {
        public string LevelName { get; }
        public string Kind      { get; }   // "line" or "text"
        public LevelState State { get; }
        public NonVisibleItem(string levelName, string kind, LevelState state)
        { LevelName = levelName; Kind = kind; State = state; }
    }

    /// <summary>
    /// ViewModel for the Diagram panel.
    /// Manages project levels and per-element (line/text) visibility preferences
    /// that are persisted to Revit Extensible Storage.
    /// </summary>
    public class DiagramViewModel : ViewModelBase
    {
        // ── Levels ────────────────────────────────────────────────────────

        public ObservableCollection<LevelInfo> Levels { get; } = new ObservableCollection<LevelInfo>();

        public void LoadLevels(IEnumerable<LevelInfo> levels)
        {
            Levels.Clear();
            foreach (var level in levels)
                Levels.Add(level);
        }

        // ── Panels ────────────────────────────────────────────────────────

        public ObservableCollection<PanelInfo> Panels { get; } = new ObservableCollection<PanelInfo>();

        public void LoadPanels(IEnumerable<Panel> panels, IEnumerable<Loop> loops, DeviceConfigStore configStore)
        {
            Panels.Clear();

            var store = configStore ?? new DeviceConfigStore();
            var loopsByPanel = (loops ?? Enumerable.Empty<Loop>())
                .GroupBy(l => l.PanelId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var p in panels)
            {
                var panelLoops  = loopsByPanel.TryGetValue(p.EntityId, out var ls) ? ls
                                  : new List<Loop>();
                var loopNames   = panelLoops
                    .OrderBy(l => l.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(l => l.DisplayName)
                    .ToList();

                int configLoopCount = 0;
                if (store.PanelAssignments.TryGetValue(p.DisplayName, out string cfgName)
                    && !string.IsNullOrEmpty(cfgName))
                {
                    var cfg = store.ControlPanels.FirstOrDefault(c => c.Name == cfgName);
                    configLoopCount = cfg?.MaxLoopCount ?? 0;
                }

                Panels.Add(new PanelInfo(p.DisplayName, p.Elevation, loopNames, configLoopCount));
            }
        }

        // ── Visibility preferences ────────────────────────────────────────

        private LevelVisibilitySettings _visibility = new LevelVisibilitySettings();

        public LevelVisibilitySettings Visibility => _visibility;

        /// <summary>Raised whenever the user changes any visibility state, to trigger a save.</summary>
        public Action VisibilityChanged { get; set; }

        public void LoadVisibility(LevelVisibilitySettings settings)
        {
            _visibility = settings ?? new LevelVisibilitySettings();
            OnPropertyChanged(nameof(Visibility));
        }

        // ── Per-element accessors ─────────────────────────────────────────

        public LevelState GetLineState(string levelName)      => _visibility.GetLineState(levelName);
        public LevelState GetTextAboveState(string levelName) => _visibility.GetTextAboveState(levelName);
        public LevelState GetTextBelowState(string levelName) => _visibility.GetTextBelowState(levelName);

        public void SetLineState(string levelName, LevelState state)
        {
            _visibility.SetLineState(levelName, state);
            OnPropertyChanged(nameof(Visibility));
            VisibilityChanged?.Invoke();
        }

        public void SetTextAboveState(string levelName, LevelState state)
        {
            _visibility.SetTextAboveState(levelName, state);
            OnPropertyChanged(nameof(Visibility));
            VisibilityChanged?.Invoke();
        }

        public void SetTextBelowState(string levelName, LevelState state)
        {
            _visibility.SetTextBelowState(levelName, state);
            OnPropertyChanged(nameof(Visibility));
            VisibilityChanged?.Invoke();
        }

        /// <summary>Returns all line/text elements that are not Visible, for the Restore panel.</summary>
        public List<NonVisibleItem> GetNonVisibleItems()
            => _visibility.States
                .Where(kv => kv.Value != LevelState.Visible)
                .Select(kv =>
                {
                    var sep   = kv.Key.LastIndexOf('|');
                    var name  = sep >= 0 ? kv.Key.Substring(0, sep) : kv.Key;
                    var kind  = sep >= 0 ? kv.Key.Substring(sep + 1) : "line";
                    return new NonVisibleItem(name, kind, kv.Value);
                })
                .OrderBy(x => x.LevelName).ThenBy(x => x.Kind)
                .ToList();
    }
}

