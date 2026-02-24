using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pulse.Core.Modules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.UI.ViewModels
{
    public readonly struct LoopLevelInfo
    {
        public double Elevation   { get; }
        public int    DeviceCount { get; }
        public LoopLevelInfo(double elevation, int deviceCount)
        { Elevation = elevation; DeviceCount = deviceCount; }
    }

    public readonly struct LoopDrawInfo
    {
        public string Name { get; }
        public IReadOnlyList<LoopLevelInfo> Levels { get; }
        public LoopDrawInfo(string name, IReadOnlyList<LoopLevelInfo> levels)
        { Name = name; Levels = levels; }
    }

    public readonly struct PanelInfo
    {
        public string Name              { get; }
        public double? Elevation         { get; }
        /// <summary>Loop draw data for each actual loop on this panel, sorted by loop name.</summary>
        public IReadOnlyList<LoopDrawInfo> LoopInfos { get; }
        /// <summary>MaxLoopCount from the assigned ControlPanelConfig (0 = no config assigned).</summary>
        public int ConfigLoopCount { get; }
        public PanelInfo(string name, double? elevation,
                         IReadOnlyList<LoopDrawInfo> loopInfos, int configLoopCount)
        { Name = name; Elevation = elevation; LoopInfos = loopInfos; ConfigLoopCount = configLoopCount; }
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
                var panelLoops  = loopsByPanel.TryGetValue(p.EntityId, out var ls) ? ls : new List<Loop>();
                var sortedLoops = panelLoops.OrderBy(l => l.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

                var loopInfos = new List<LoopDrawInfo>();
                foreach (var loop in sortedLoops)
                {
                    var levelMap = new Dictionary<double, int>();
                    foreach (var d in loop.Devices)
                    {
                        if (!d.Elevation.HasValue) continue;
                        double key = Math.Round(d.Elevation.Value, 3);
                        levelMap[key] = levelMap.TryGetValue(key, out int cnt) ? cnt + 1 : 1;
                    }
                    var levelInfos = levelMap
                        .Select(kv => new LoopLevelInfo(kv.Key, kv.Value))
                        .OrderBy(x => x.Elevation)
                        .ToList();
                    loopInfos.Add(new LoopDrawInfo(loop.DisplayName, levelInfos));
                }

                int configLoopCount = 0;
                if (store.PanelAssignments.TryGetValue(p.DisplayName, out string cfgName)
                    && !string.IsNullOrEmpty(cfgName))
                {
                    var cfg = store.ControlPanels.FirstOrDefault(c => c.Name == cfgName);
                    configLoopCount = cfg?.MaxLoopCount ?? 0;
                }

                Panels.Add(new PanelInfo(p.DisplayName, p.Elevation, loopInfos, configLoopCount));
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

