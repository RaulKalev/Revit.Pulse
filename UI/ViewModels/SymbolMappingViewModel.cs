using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.Core.Modules;

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

        /// <summary>Filtered rows shown in the grid.</summary>
        public ObservableCollection<SymbolMappingEntryViewModel> FilteredEntries { get; }
            = new ObservableCollection<SymbolMappingEntryViewModel>();

        /// <summary>
        /// Available symbol names for the ComboBox dropdown.
        /// Populated when symbols are implemented; editable so users can type freely now.
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

        // ─── Commands ────────────────────────────────────────────────────────
        public ICommand SaveCommand     { get; }
        public ICommand CancelCommand   { get; }
        public ICommand ClearAllCommand { get; }

        // ─── Constructor ─────────────────────────────────────────────────────

        /// <param name="data">Current module data. May be null before first Refresh.</param>
        /// <param name="existingMappings">Previously saved mappings: deviceType → symbol.</param>
        public SymbolMappingViewModel(ModuleData data, Dictionary<string, string> existingMappings)
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
