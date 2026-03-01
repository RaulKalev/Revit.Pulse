using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Pulse.Core.Boq;
using Pulse.Core.Modules;
using Pulse.Revit.Services;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// Root ViewModel for the modeless BOQ window.
    ///
    /// Responsibilities:
    ///   • Convert <see cref="ModuleData"/> → flat <see cref="BoqRowViewModel"/> rows
    ///   • Manage column visibility, order, and custom columns
    ///   • Apply grouping and sorting via <see cref="ICollectionView"/>
    ///   • Persist settings through <see cref="BoqSettingsService"/>
    ///   • Export / import settings as JSON
    ///
    /// The view subscribes to <see cref="ColumnsChanged"/> to rebuild DataGrid columns
    /// whenever the column layout is modified.
    /// </summary>
    public class BoqWindowViewModel : ViewModelBase
    {
        // ── Dependencies ─────────────────────────────────────────────────────

        private readonly IBoqDataProvider _dataProvider;
        private readonly BoqSettingsService _settingsService;

        // Delegate used to trigger a fresh data collection from Revit
        private readonly Action<Action<ModuleData>, Action<Exception>> _requestRefresh;

        // ── Settings ─────────────────────────────────────────────────────────

        private BoqSettings _settings;

        /// <summary>
        /// Returns the current in-memory settings (synced to the latest save).
        /// Used by MainViewModel to update its cached copy when the window closes.
        /// </summary>
        public BoqSettings CurrentSettings => _settings;

        // ── Column state ─────────────────────────────────────────────────────

        private readonly ObservableCollection<BoqColumnViewModel> _allColumns =
            new ObservableCollection<BoqColumnViewModel>();

        /// <summary>All columns (standard + discovered + custom).  Used to drive the settings panel.</summary>
        public ObservableCollection<BoqColumnViewModel> AllColumns => _allColumns;

        // ── Data ─────────────────────────────────────────────────────────────

        private ModuleData _currentData;

        private readonly ObservableCollection<BoqRowViewModel> _rowsSource =
            new ObservableCollection<BoqRowViewModel>();

        // Holds all individual (non-aggregated) rows from the last data load.
        // RebuildDisplayRows() derives _rowsSource from this list.
        private readonly List<BoqRowViewModel> _allRawRows = new List<BoqRowViewModel>();

        private ICollectionView _rows;
        /// <summary>The filtered / sorted / grouped view of BOQ rows — bind DataGrid.ItemsSource here.</summary>
        public ICollectionView Rows => _rows;

        // ── Status ───────────────────────────────────────────────────────────

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value);
        }

        private int _rowCount;
        public int RowCount
        {
            get => _rowCount;
            set => SetField(ref _rowCount, value);
        }

        // ── Settings panel visibility ─────────────────────────────────────────

        private bool _isSettingsPanelOpen;
        public bool IsSettingsPanelOpen
        {
            get => _isSettingsPanelOpen;
            set => SetField(ref _isSettingsPanelOpen, value);
        }

        // ── Column search box (settings panel) ───────────────────────────────

        private string _columnSearchText = string.Empty;
        public string ColumnSearchText
        {
            get => _columnSearchText;
            set
            {
                if (SetField(ref _columnSearchText, value))
                    FilteredColumns.Refresh();
            }
        }

        /// <summary>Filtered view of AllColumns for the settings panel search box.</summary>
        public ICollectionView FilteredColumns { get; }

        // ── Selected column for move-up/move-down ─────────────────────────────

        private BoqColumnViewModel _selectedColumn;
        public BoqColumnViewModel SelectedColumn
        {
            get => _selectedColumn;
            set => SetField(ref _selectedColumn, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────

        public ICommand RefreshCommand          { get; }
        public ICommand ToggleSettingsPanelCommand { get; }
        public ICommand SaveSettingsCommand     { get; }
        public ICommand ExportSettingsCommand   { get; }
        public ICommand ImportSettingsCommand   { get; }
        public ICommand AddCustomColumnCommand  { get; }
        public ICommand EditCustomColumnCommand { get; }
        public ICommand DeleteCustomColumnCommand { get; }
        public ICommand MoveColumnUpCommand     { get; }
        public ICommand MoveColumnDownCommand   { get; }
        public ICommand AddGroupingRuleCommand  { get; }
        public ICommand RemoveGroupingRuleCommand { get; }
        public ICommand AddSortingRuleCommand   { get; }
        public ICommand RemoveSortingRuleCommand { get; }
        public ICommand ApplyViewRulesCommand   { get; }

        // ── Grouping / sorting editors ────────────────────────────────────────

        public ObservableCollection<string> AvailableFieldKeys { get; } = new ObservableCollection<string>();

        public ObservableCollection<BoqGroupingRule> GroupingRules  { get; } = new ObservableCollection<BoqGroupingRule>();
        public ObservableCollection<BoqSortingRule>  SortingRules   { get; } = new ObservableCollection<BoqSortingRule>();

        private BoqGroupingRule _selectedGroupingRule;
        public BoqGroupingRule SelectedGroupingRule
        {
            get => _selectedGroupingRule;
            set => SetField(ref _selectedGroupingRule, value);
        }

        private BoqSortingRule _selectedSortingRule;
        public BoqSortingRule SelectedSortingRule
        {
            get => _selectedSortingRule;
            set => SetField(ref _selectedSortingRule, value);
        }

        // ── Column-changed notification (view subscribes to rebuild DataGrid) ──

        /// <summary>Fires when the visible column layout changes and the DataGrid needs rebuilding.</summary>
        public event EventHandler ColumnsChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public BoqWindowViewModel(
            IBoqDataProvider dataProvider,
            BoqSettingsService settingsService,
            BoqSettings initialSettings,
            ModuleData initialData,
            Action<Action<ModuleData>, Action<Exception>> requestRefresh)
        {
            _dataProvider    = dataProvider    ?? throw new ArgumentNullException(nameof(dataProvider));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _requestRefresh  = requestRefresh  ?? throw new ArgumentNullException(nameof(requestRefresh));

            // Build initial settings (or defaults if none loaded from ES)
            _settings = initialSettings ?? BuildDefaultSettings();

            // Set up the CollectionViewSource
            _rows = CollectionViewSource.GetDefaultView(_rowsSource);

            // Set up filtered column view
            FilteredColumns = CollectionViewSource.GetDefaultView(_allColumns);
            FilteredColumns.Filter = obj =>
            {
                if (obj is BoqColumnViewModel col)
                {
                    if (string.IsNullOrWhiteSpace(_columnSearchText)) return true;
                    return col.Header.IndexOf(_columnSearchText, StringComparison.OrdinalIgnoreCase) >= 0
                        || col.FieldKey.IndexOf(_columnSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                return true;
            };

            // Wire up grouping/sorting rules from settings
            foreach (var r in _settings.GroupingRules.OrderBy(r => r.Priority))
                GroupingRules.Add(r);
            foreach (var r in _settings.SortingRules.OrderBy(r => r.Priority))
                SortingRules.Add(r);

            // Commands
            RefreshCommand             = new RelayCommand(ExecuteRefresh, () => !IsLoading);
            ToggleSettingsPanelCommand = new RelayCommand(() => IsSettingsPanelOpen = !IsSettingsPanelOpen);
            SaveSettingsCommand        = new RelayCommand(ExecuteSaveSettings);
            ExportSettingsCommand      = new RelayCommand(ExecuteExportSettings);
            ImportSettingsCommand      = new RelayCommand(ExecuteImportSettings);
            AddCustomColumnCommand     = new RelayCommand(ExecuteAddCustomColumn);
            EditCustomColumnCommand    = new RelayCommand(ExecuteEditCustomColumn,
                                            () => SelectedColumn?.IsCustom == true);
            DeleteCustomColumnCommand  = new RelayCommand(ExecuteDeleteCustomColumn,
                                            () => SelectedColumn?.IsCustom == true);
            MoveColumnUpCommand        = new RelayCommand(ExecuteMoveColumnUp,
                                            () => SelectedColumn != null);
            MoveColumnDownCommand      = new RelayCommand(ExecuteMoveColumnDown,
                                            () => SelectedColumn != null);
            AddGroupingRuleCommand     = new RelayCommand(ExecuteAddGroupingRule);
            RemoveGroupingRuleCommand  = new RelayCommand(ExecuteRemoveGroupingRule,
                                            () => SelectedGroupingRule != null);
            AddSortingRuleCommand      = new RelayCommand(ExecuteAddSortingRule);
            RemoveSortingRuleCommand   = new RelayCommand(ExecuteRemoveSortingRule,
                                            () => SelectedSortingRule != null);
            ApplyViewRulesCommand      = new RelayCommand(ExecuteApplyViewRules);

            // Wire visibility-change notifications so AvailableFieldKeys stays in sync
            _allColumns.CollectionChanged += OnAllColumnsCollectionChanged;

            // Load data if provided
            if (initialData != null)
                ApplyData(initialData);
        }

        // ── Column-visibility tracking ─────────────────────────────────────────

        private void OnAllColumnsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Rewire all current items after a Clear
                foreach (var col in _allColumns)
                    col.PropertyChanged += OnColumnPropertyChanged;
            }
            else
            {
                if (e.OldItems != null)
                    foreach (BoqColumnViewModel col in e.OldItems)
                        col.PropertyChanged -= OnColumnPropertyChanged;
                if (e.NewItems != null)
                    foreach (BoqColumnViewModel col in e.NewItems)
                        col.PropertyChanged += OnColumnPropertyChanged;
            }
        }

        private void OnColumnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BoqColumnViewModel.IsVisible))
                RebuildAvailableFieldKeys();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the ordered list of visible columns so the code-behind can build
        /// DataGrid columns.  Called from ColumnsChanged handler and on initial load.
        /// </summary>
        public IReadOnlyList<BoqColumnViewModel> GetVisibleColumnsOrdered()
        {
            var cols = _allColumns
                .Where(c => c.IsVisible)
                .OrderBy(c => c.DisplayOrder < 0 ? int.MaxValue : c.DisplayOrder)
                .ThenBy(c => c.Header)
                .ToList();

            // When grouping is active, append a synthetic Count column at the far right.
            if (GroupingRules.Count > 0)
                cols.Add(new BoqColumnViewModel(
                    new BoqColumnDefinition("_Count", "Count", isVisible: true)));

            return cols;
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private void ExecuteRefresh()
        {
            IsLoading  = true;
            StatusText = "Collecting data from Revit…";

            _requestRefresh(
                data => Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ApplyData(data);
                    IsLoading  = false;
                    StatusText = $"{RowCount} rows | {_currentData?.Devices?.Count ?? 0} devices";
                }),
                ex => Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsLoading  = false;
                    StatusText = $"Refresh error: {ex.Message}";
                }));
        }

        private void ApplyData(ModuleData data)
        {
            _currentData = data;

            var items = _dataProvider.GetItems(data);
            var discoveredKeys = _dataProvider.DiscoverParameterKeys(data);

            // Merge any newly-discovered parameter keys into column definitions
            MergeDiscoveredColumns(discoveredKeys);

            // Cache all individual rows; RebuildDisplayRows will aggregate as needed
            var customDefs = _settings.CustomColumns.AsReadOnly();
            _allRawRows.Clear();
            foreach (var item in items)
                _allRawRows.Add(new BoqRowViewModel(item, customDefs));

            // Update available field keys for grouping/sorting dropdowns
            RebuildAvailableFieldKeys();

            // Aggregate/sort + rebuild DataGrid columns
            ApplyViewRules();

            StatusText = $"{RowCount} rows loaded.";
        }

        // ── Column management ─────────────────────────────────────────────────

        private void MergeDiscoveredColumns(IReadOnlyList<string> parameterKeys)
        {
            // Build lookup of existing columns
            var existing = new HashSet<string>(
                _allColumns.Select(c => c.FieldKey),
                StringComparer.OrdinalIgnoreCase);

            // Ensure standard columns exist, restoring persisted visibility/order where available
            foreach (var standard in StandardColumns())
            {
                if (!existing.Contains(standard.FieldKey))
                {
                    var persisted = _settings.VisibleColumns
                        .FirstOrDefault(c => string.Equals(c.FieldKey, standard.FieldKey, StringComparison.OrdinalIgnoreCase));

                    if (persisted != null)
                    {
                        standard.IsVisible    = persisted.IsVisible;
                        standard.DisplayOrder = persisted.DisplayOrder;
                    }

                    _allColumns.Add(new BoqColumnViewModel(standard));
                    existing.Add(standard.FieldKey);
                }
            }

            // Add newly discovered parameter columns
            foreach (var key in parameterKeys)
            {
                if (!existing.Contains(key))
                {
                    var def = new BoqColumnDefinition(key, key, isVisible: false)
                    {
                        IsDiscovered = true
                    };

                    // Check if this key was previously persisted in settings
                    var persisted = _settings.VisibleColumns
                        .FirstOrDefault(c => string.Equals(c.FieldKey, key, StringComparison.OrdinalIgnoreCase));

                    if (persisted != null)
                    {
                        def.IsVisible    = persisted.IsVisible;
                        def.DisplayOrder = persisted.DisplayOrder;
                    }

                    _allColumns.Add(new BoqColumnViewModel(def));
                    existing.Add(key);
                }
            }

            // Ensure custom columns are present
            foreach (var custom in _settings.CustomColumns)
            {
                if (!existing.Contains(custom.ColumnKey))
                {
                    var def = new BoqColumnDefinition(custom.ColumnKey, custom.Header, isVisible: true)
                    {
                        IsCustom = true
                    };
                    _allColumns.Add(new BoqColumnViewModel(def));
                    existing.Add(custom.ColumnKey);
                }
            }

            // Apply persisted ordering from settings
            ApplyPersistedColumnOrder();
        }

        private void ApplyPersistedColumnOrder()
        {
            if (_settings.ColumnOrder == null || _settings.ColumnOrder.Count == 0) return;

            for (int i = 0; i < _settings.ColumnOrder.Count; i++)
            {
                var key = _settings.ColumnOrder[i];
                var col = _allColumns.FirstOrDefault(c =>
                    string.Equals(c.FieldKey, key, StringComparison.OrdinalIgnoreCase));
                if (col != null)
                    col.DisplayOrder = i;
            }
        }

        private static IEnumerable<BoqColumnDefinition> StandardColumns()
        {
            int order = 0;
            yield return new BoqColumnDefinition("Category",   "Category",    isVisible: true)  { DisplayOrder = order++ };
            yield return new BoqColumnDefinition("FamilyName", "Family",      isVisible: true)  { DisplayOrder = order++ };
            yield return new BoqColumnDefinition("TypeName",   "Type",        isVisible: true)  { DisplayOrder = order++ };
            yield return new BoqColumnDefinition("Level",      "Level",       isVisible: true)  { DisplayOrder = order++ };
            yield return new BoqColumnDefinition("Panel",      "Panel",       isVisible: true)  { DisplayOrder = order++ };
            yield return new BoqColumnDefinition("Loop",       "Loop",        isVisible: true)  { DisplayOrder = order++ };
        }

        private void RebuildAvailableFieldKeys()
        {
            var newKeys = _allColumns
                .Where(c => c.IsVisible)
                .OrderBy(c => c.Header)
                .Select(c => c.FieldKey)
                .ToList();

            // Add new keys BEFORE removing stale ones.
            // Calling AvailableFieldKeys.Clear() when ComboBoxes have TwoWay
            // SelectedItem bindings causes WPF to push null back into every
            // BoqGroupingRule.FieldKey / BoqSortingRule.FieldKey, corrupting
            // the persisted rules on every Refresh.
            foreach (var key in newKeys)
                if (!AvailableFieldKeys.Contains(key))
                    AvailableFieldKeys.Add(key);

            for (int i = AvailableFieldKeys.Count - 1; i >= 0; i--)
                if (!newKeys.Contains(AvailableFieldKeys[i]))
                    AvailableFieldKeys.RemoveAt(i);
        }

        // ── Column move ───────────────────────────────────────────────────────

        private void ExecuteMoveColumnUp()
        {
            if (SelectedColumn == null) return;
            var ordered = _allColumns
                .OrderBy(c => c.DisplayOrder < 0 ? int.MaxValue : c.DisplayOrder)
                .ThenBy(c => c.Header)
                .ToList();
            int idx = ordered.IndexOf(SelectedColumn);
            if (idx <= 0) return;

            int a = ordered[idx].DisplayOrder;
            int b = ordered[idx - 1].DisplayOrder;
            ordered[idx].DisplayOrder     = b < 0 ? idx - 1 : b;
            ordered[idx - 1].DisplayOrder = a < 0 ? idx     : a;

            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteMoveColumnDown()
        {
            if (SelectedColumn == null) return;
            var ordered = _allColumns
                .OrderBy(c => c.DisplayOrder < 0 ? int.MaxValue : c.DisplayOrder)
                .ThenBy(c => c.Header)
                .ToList();
            int idx = ordered.IndexOf(SelectedColumn);
            if (idx < 0 || idx >= ordered.Count - 1) return;

            int a = ordered[idx].DisplayOrder;
            int b = ordered[idx + 1].DisplayOrder;
            ordered[idx].DisplayOrder     = b < 0 ? idx + 1 : b;
            ordered[idx + 1].DisplayOrder = a < 0 ? idx     : a;

            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Custom column CRUD ────────────────────────────────────────────────

        private void ExecuteAddCustomColumn()
        {
            var editor = new BoqCustomColumnEditorViewModel();
            foreach (var key in AvailableFieldKeys)
                editor.AvailableSourceKeys.Add(key);

            var dialog = new BoqCustomColumnEditorWindow(editor)
            {
                Owner = Application.Current?.MainWindow
            };
            bool? result = dialog.ShowDialog();
            if (result != true || !editor.IsValid) return;

            var def = editor.ToDefinition();
            _settings.CustomColumns.Add(def);

            // Add column definition
            var colDef = new BoqColumnDefinition(def.ColumnKey, def.Header, isVisible: true)
            {
                IsCustom     = true,
                DisplayOrder = _allColumns.Count
            };
            _allColumns.Add(new BoqColumnViewModel(colDef));
            RebuildAvailableFieldKeys();

            // Rebuild rows to include the new custom column
            RebuildRows();
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteEditCustomColumn()
        {
            if (SelectedColumn?.IsCustom != true) return;
            var existing = _settings.CustomColumns
                .FirstOrDefault(c => string.Equals(c.ColumnKey, SelectedColumn.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            var editor = new BoqCustomColumnEditorViewModel();
            foreach (var key in AvailableFieldKeys)
                editor.AvailableSourceKeys.Add(key);
            editor.LoadFrom(existing);

            var dialog = new BoqCustomColumnEditorWindow(editor)
            {
                Owner = Application.Current?.MainWindow
            };
            bool? result = dialog.ShowDialog();
            if (result != true || !editor.IsValid) return;

            var updated = editor.ToDefinition();
            _settings.CustomColumns.Remove(existing);
            _settings.CustomColumns.Add(updated);

            // Update header in AllColumns
            var colVm = _allColumns
                .FirstOrDefault(c => string.Equals(c.FieldKey, SelectedColumn.FieldKey, StringComparison.OrdinalIgnoreCase));
            // Force rebuild (colVm header is read-only — replace the VM)
            if (colVm != null)
            {
                int idx = _allColumns.IndexOf(colVm);
                _allColumns[idx] = new BoqColumnViewModel(
                    new BoqColumnDefinition(updated.ColumnKey, updated.Header, true) { IsCustom = true, DisplayOrder = colVm.DisplayOrder });
            }

            RebuildRows();
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteDeleteCustomColumn()
        {
            if (SelectedColumn?.IsCustom != true) return;

            _settings.CustomColumns.RemoveAll(c =>
                string.Equals(c.ColumnKey, SelectedColumn.FieldKey, StringComparison.OrdinalIgnoreCase));

            var colVm = _allColumns.FirstOrDefault(c =>
                string.Equals(c.FieldKey, SelectedColumn.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (colVm != null)
                _allColumns.Remove(colVm);

            SelectedColumn = null;
            RebuildRows();
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RebuildRows()
        {
            if (_currentData == null) return;
            var customDefs = _settings.CustomColumns.AsReadOnly();
            var items = _dataProvider.GetItems(_currentData);
            _allRawRows.Clear();
            foreach (var item in items)
                _allRawRows.Add(new BoqRowViewModel(item, customDefs));
            ApplyViewRules();
        }

        // ── Grouping & sorting ────────────────────────────────────────────────

        private void ExecuteAddGroupingRule()
        {
            if (AvailableFieldKeys.Count == 0) return;
            GroupingRules.Add(new BoqGroupingRule
            {
                FieldKey    = AvailableFieldKeys.First(),
                DisplayName = AvailableFieldKeys.First(),
                Priority    = GroupingRules.Count
            });
        }

        private void ExecuteRemoveGroupingRule()
        {
            if (SelectedGroupingRule == null) return;
            GroupingRules.Remove(SelectedGroupingRule);
            SelectedGroupingRule = null;
        }

        private void ExecuteAddSortingRule()
        {
            if (AvailableFieldKeys.Count == 0) return;
            SortingRules.Add(new BoqSortingRule
            {
                FieldKey    = AvailableFieldKeys.First(),
                DisplayName = AvailableFieldKeys.First(),
                Direction   = BoqSortDirection.Ascending,
                Priority    = SortingRules.Count
            });
        }

        private void ExecuteRemoveSortingRule()
        {
            if (SelectedSortingRule == null) return;
            SortingRules.Remove(SelectedSortingRule);
            SelectedSortingRule = null;
        }

        private void ExecuteApplyViewRules()
        {
            ApplyViewRules();
        }

        private void ApplyViewRules()
        {
            if (_rows == null) return;

            // Build aggregated (or pass-through) row source before applying sort
            RebuildDisplayRows();

            using (_rows.DeferRefresh())
            {
                _rows.GroupDescriptions.Clear();
                _rows.SortDescriptions.Clear();

                foreach (var rule in SortingRules.OrderBy(r => r.Priority))
                {
                    _rows.SortDescriptions.Add(new SortDescription(
                        $"[{rule.FieldKey}]",
                        rule.Direction == BoqSortDirection.Ascending
                            ? ListSortDirection.Ascending
                            : ListSortDirection.Descending));
                }
            }

            // Column layout changes when Count column appears/disappears
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Populates <see cref="_rowsSource"/> from <see cref="_allRawRows"/>.
        /// When grouping rules are active the rows are aggregated into one row per
        /// unique key combination, with <see cref="BoqRowViewModel.Count"/> set to
        /// the number of source rows in that group.
        /// </summary>
        private void RebuildDisplayRows()
        {
            _rowsSource.Clear();

            if (GroupingRules.Count == 0)
            {
                foreach (var row in _allRawRows)
                {
                    row.Count = 1;
                    _rowsSource.Add(row);
                }
            }
            else
            {
                var groupKeys = GroupingRules
                    .OrderBy(r => r.Priority)
                    .Select(r => r.FieldKey)
                    .ToList();

                var groups = _allRawRows
                    .GroupBy(row => string.Join("\x00",
                        groupKeys.Select(k => (row[k] ?? string.Empty).ToString())));

                foreach (var group in groups)
                {
                    var representative = group.First();
                    representative.Count = group.Count();
                    _rowsSource.Add(representative);
                }
            }

            RowCount = _rowsSource.Count;
        }

        // ── Settings persist / export / import ────────────────────────────────

        private void ExecuteSaveSettings()
        {
            PersistSettingsToModel();
            _settingsService.Save(_settings,
                onSaved: () => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = "Settings saved."),
                onError: ex => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Save failed: {ex.Message}"));
        }

        private void ExecuteExportSettings()
        {
            var dlg = new SaveFileDialog
            {
                Title            = "Export BOQ Settings",
                Filter           = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt       = ".json",
                FileName         = $"BOQ_Settings_{_settings.ModuleKey}.json",
                OverwritePrompt  = true
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                PersistSettingsToModel();
                _settingsService.ExportToJson(_settings, dlg.FileName);
                StatusText = $"Settings exported to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private void ExecuteImportSettings()
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Import BOQ Settings",
                Filter      = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt  = ".json"
            };

            if (dlg.ShowDialog() != true) return;

            var imported = _settingsService.ImportFromJson(dlg.FileName);
            if (imported == null)
            {
                StatusText = "Import failed — invalid or missing file.";
                return;
            }

            // Apply imported settings
            _settings = imported;
            _allColumns.Clear();
            GroupingRules.Clear();
            SortingRules.Clear();

            foreach (var r in _settings.GroupingRules.OrderBy(r => r.Priority))
                GroupingRules.Add(r);
            foreach (var r in _settings.SortingRules.OrderBy(r => r.Priority))
                SortingRules.Add(r);

            if (_currentData != null)
                ApplyData(_currentData);

            // Persist to ES
            _settingsService.Save(_settings);
            StatusText = "Settings imported and applied.";
        }

        /// <summary>
        /// Sync the in-memory <see cref="BoqSettings"/> from the current ViewModel state
        /// before persisting.
        /// </summary>
        private void PersistSettingsToModel()
        {
            _settings.VisibleColumns = _allColumns
                .Select(c => c.ToDefinition())
                .ToList();

            _settings.ColumnOrder = _allColumns
                .OrderBy(c => c.DisplayOrder < 0 ? int.MaxValue : c.DisplayOrder)
                .ThenBy(c => c.Header)
                .Select(c => c.FieldKey)
                .ToList();

            _settings.GroupingRules = GroupingRules
                .Select((r, i) => { r.Priority = i; return r; })
                .ToList();

            _settings.SortingRules = SortingRules
                .Select((r, i) => { r.Priority = i; return r; })
                .ToList();
        }

        // ── Defaults ──────────────────────────────────────────────────────────

        private static BoqSettings BuildDefaultSettings()
        {
            var settings = new BoqSettings
            {
                ModuleKey = "FireAlarm",
                SettingsVersion = "1.0"
            };

            // Default visible standard columns
            int order = 0;
            foreach (var col in new[]
            {
                new BoqColumnDefinition("Category",   "Category",   true)  { DisplayOrder = order++ },
                new BoqColumnDefinition("FamilyName", "Family",     true)  { DisplayOrder = order++ },
                new BoqColumnDefinition("TypeName",   "Type",       true)  { DisplayOrder = order++ },
                new BoqColumnDefinition("Level",      "Level",      true)  { DisplayOrder = order++ },
                new BoqColumnDefinition("Panel",      "Panel",      true)  { DisplayOrder = order++ },
                new BoqColumnDefinition("Loop",       "Loop",       true)  { DisplayOrder = order++ },
            })
            {
                settings.VisibleColumns.Add(col);
                settings.ColumnOrder.Add(col.FieldKey);
            }

            return settings;
        }
    }
}
