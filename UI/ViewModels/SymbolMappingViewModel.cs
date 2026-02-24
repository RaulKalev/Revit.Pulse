using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Represents one unique DeviceType row in the symbol-mapping grid.
    /// All devices of this type in the project share the same symbol assignment.
    /// </summary>
    public class SymbolMappingEntryViewModel : ViewModelBase
    {
        /// <summary>The device type string (e.g. "Smoke Detector").</summary>
        public string DeviceType { get; }

        /// <summary>How many devices of this type exist in the current document.</summary>
        public int Count { get; }

        private string _symbolName;
        /// <summary>Symbol key assigned to every device of this type. Empty = no assignment.</summary>
        public string SymbolName
        {
            get => _symbolName;
            set
            {
                if (_symbolName == value) return;
                _symbolName = value;
                OnPropertyChanged(nameof(SymbolName));
                OnPropertyChanged(nameof(HasSymbol));
            }
        }

        /// <summary>True when a symbol has been mapped.</summary>
        public bool HasSymbol => !string.IsNullOrWhiteSpace(_symbolName);

        public ICommand ClearSymbolCommand { get; }

        public SymbolMappingEntryViewModel(string deviceType, int count, string initialSymbol)
        {
            DeviceType  = deviceType    ?? string.Empty;
            Count       = count;
            _symbolName = initialSymbol ?? string.Empty;

            ClearSymbolCommand = new RelayCommand(_ => SymbolName = string.Empty);
        }
    }

    /// <summary>
    /// ViewModel for the Symbol Mapping popup window.
    /// Shows one row per unique DeviceType found in the current document.
    /// Mappings are keyed by DeviceType and persisted to Extensible Storage.
    /// </summary>
    public class SymbolMappingViewModel : ViewModelBase
    {
        private readonly List<SymbolMappingEntryViewModel> _allEntries
            = new List<SymbolMappingEntryViewModel>();

        /// <summary>Full definitions from the custom symbol library (not just names).</summary>
        private readonly List<CustomSymbolDefinition> _symbolLibrary
            = new List<CustomSymbolDefinition>();

        /// <summary>Filtered rows shown in the grid.</summary>
        public ObservableCollection<SymbolMappingEntryViewModel> FilteredEntries { get; }
            = new ObservableCollection<SymbolMappingEntryViewModel>();

        /// <summary>
        /// Available symbol names for the ComboBox dropdown, sourced from the custom symbol library.
        /// Users can also type freely if no matching symbol exists.
        /// </summary>
        public ObservableCollection<string> AvailableSymbols { get; }
            = new ObservableCollection<string>();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private int _mappedCount;
        public int MappedCount
        {
            get => _mappedCount;
            set => SetField(ref _mappedCount, value);
        }

        public int TotalCount => _allEntries.Count;

        // ─── Events ──────────────────────────────────────────────────────────

        /// <summary>Raised on Save. Carries deviceType → symbol dictionary.</summary>
        public event Action<Dictionary<string, string>> Saved;

        /// <summary>Raised on Cancel/Close.</summary>
        public event Action Cancelled;

        /// <summary>
        /// Raised when the user clicks the "+ New Symbol" button.
        /// The caller (Window code-behind or MainViewModel) should open the designer and,
        /// on success, call <see cref="AddSymbolToLibrary"/> with the new definition.
        /// </summary>
        public event Action NewSymbolRequested;

        /// <summary>
        /// Raised when the user clicks the edit (pencil) button on a symbol row.
        /// Carries the full definition so the caller can open the designer pre-populated.
        /// </summary>
        public event Action<CustomSymbolDefinition> EditSymbolRequested;

        // ─── Commands ────────────────────────────────────────────────────────
        public ICommand SaveCommand            { get; }
        public ICommand CancelCommand          { get; }
        public ICommand ClearAllCommand        { get; }
        public ICommand CreateNewSymbolCommand { get; }
        /// <summary>Parameterised by symbol name (string). Fires <see cref="EditSymbolRequested"/> with the matching definition.</summary>
        public ICommand EditSymbolCommand      { get; }

        // ─── Constructor ─────────────────────────────────────────────────────

        /// <param name="data">Current module data. May be null before first Refresh.</param>
        /// <param name="existingMappings">Previously saved mappings: deviceType → symbol.</param>
        /// <param name="symbolLibrary">Custom symbol definitions to populate the dropdown.</param>
        public SymbolMappingViewModel(
            ModuleData data,
            Dictionary<string, string> existingMappings,
            IEnumerable<CustomSymbolDefinition> symbolLibrary = null)
        {
            if (existingMappings == null)
                existingMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (data != null)
            {
                // Group devices by DeviceType, sorted alphabetically
                var groups = data.Devices
                    .GroupBy(
                        d => string.IsNullOrWhiteSpace(d.DeviceType) ? "(Unknown)" : d.DeviceType.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key);

                foreach (var group in groups)
                {
                    existingMappings.TryGetValue(group.Key, out var existingSymbol);
                    _allEntries.Add(new SymbolMappingEntryViewModel(group.Key, group.Count(), existingSymbol));
                }
            }

            // Populate symbol dropdown from library
            if (symbolLibrary != null)
            {
                foreach (var sym in symbolLibrary.OrderBy(s => s.Name))
                {
                    _symbolLibrary.Add(sym);
                    AvailableSymbols.Add(sym.Name);
                }
            }

            ApplyFilter();
            RefreshMappedCount();

            SaveCommand   = new RelayCommand(_ => Saved?.Invoke(GetMappings()));
            CancelCommand = new RelayCommand(_ => Cancelled?.Invoke());
            ClearAllCommand = new RelayCommand(_ =>
            {
                foreach (var e in _allEntries)
                    e.SymbolName = string.Empty;
                RefreshMappedCount();
            });
            CreateNewSymbolCommand = new RelayCommand(_ => NewSymbolRequested?.Invoke());
            EditSymbolCommand = new RelayCommand(
                p =>
                {
                    var name = p as string;
                    if (string.IsNullOrEmpty(name)) return;
                    var def = _symbolLibrary.FirstOrDefault(s =>
                        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (def != null) EditSymbolRequested?.Invoke(def);
                },
                p => p is string s && !string.IsNullOrEmpty(s));
        }

        /// <summary>
        /// Called after the user successfully creates a symbol in the designer.
        /// Adds the name to the dropdown so they can immediately assign it.
        /// </summary>
        public void AddSymbolToLibrary(CustomSymbolDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name)) return;
            _symbolLibrary.RemoveAll(s => s.Id == definition.Id);
            _symbolLibrary.Add(definition);
            if (!AvailableSymbols.Contains(definition.Name))
                AvailableSymbols.Add(definition.Name);
        }

        /// <summary>
        /// Replaces an existing symbol definition in the library (used after editing).
        /// Updates the dropdown and any row that was assigned the old name.
        /// </summary>
        public void ReplaceSymbolInLibrary(CustomSymbolDefinition oldDef, CustomSymbolDefinition newDef)
        {
            if (oldDef == null || newDef == null) return;

            // Update backing library list
            var idx = _symbolLibrary.FindIndex(s => s.Id == oldDef.Id);
            if (idx >= 0) _symbolLibrary[idx] = newDef;
            else          _symbolLibrary.Add(newDef);

            // Sync the dropdown: replace the old name with the new one
            bool nameChanged = !string.Equals(oldDef.Name, newDef.Name, StringComparison.OrdinalIgnoreCase);
            int nameIdx = AvailableSymbols.IndexOf(oldDef.Name);
            if (nameIdx >= 0)
            {
                if (nameChanged) AvailableSymbols[nameIdx] = newDef.Name;
            }
            else if (!AvailableSymbols.Contains(newDef.Name))
            {
                AvailableSymbols.Add(newDef.Name);
            }

            // Re-point any row that had the old name
            if (nameChanged)
            {
                foreach (var e in _allEntries)
                {
                    if (string.Equals(e.SymbolName, oldDef.Name, StringComparison.OrdinalIgnoreCase))
                        e.SymbolName = newDef.Name;
                }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            FilteredEntries.Clear();
            var query = (_searchText ?? "").Trim();
            foreach (var entry in _allEntries)
            {
                if (string.IsNullOrEmpty(query) || MatchesFilter(entry, query))
                    FilteredEntries.Add(entry);
            }
            RefreshMappedCount();
        }

        private static bool MatchesFilter(SymbolMappingEntryViewModel entry, string query)
        {
            return ContainsIgnoreCase(entry.DeviceType, query)
                || ContainsIgnoreCase(entry.SymbolName, query);
        }

        private static bool ContainsIgnoreCase(string source, string value)
            => source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private void RefreshMappedCount()
        {
            MappedCount = _allEntries.Count(e => !string.IsNullOrWhiteSpace(e.SymbolName));
            OnPropertyChanged(nameof(TotalCount));
        }

        private Dictionary<string, string> GetMappings()
        {
            RefreshMappedCount();
            return _allEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.SymbolName))
                .ToDictionary(
                    e => e.DeviceType,
                    e => e.SymbolName.Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
