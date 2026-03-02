using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.Core.Boq;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// ViewModel for the parameter picker dialog.
    ///
    /// Available — every parameter name found in the Revit category that is NOT
    ///             currently visible in the BOQ (may include brand-new keys).
    /// Chosen    — columns currently visible in the BOQ DataGrid.
    ///
    /// Moving items calls <see cref="BoqColumnViewModel.IsVisible"/> directly.
    /// Brand-new keys (unknown to AllColumns) produce new <see cref="BoqColumnViewModel"/>
    /// instances exposed via <see cref="NewColumns"/> so the caller can add them to AllColumns.
    /// </summary>
    public class BoqParameterPickerViewModel : ViewModelBase
    {
        // ── Internal tracking ─────────────────────────────────────────────────

        /// <summary>FieldKey → original IsVisible snapshot (for Cancel rollback).</summary>
        private readonly Dictionary<string, bool> _originalVisibility =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>FieldKey → BoqColumnViewModel for all known existing columns.</summary>
        private readonly Dictionary<string, BoqColumnViewModel> _existingCols =
            new Dictionary<string, BoqColumnViewModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>New BoqColumnViewModels created for keys not yet in AllColumns.</summary>
        private readonly Dictionary<string, BoqColumnViewModel> _newColVMs =
            new Dictionary<string, BoqColumnViewModel>(StringComparer.OrdinalIgnoreCase);
        // ── Collections ───────────────────────────────────────────────────────

        /// <summary>Parameters available in Revit but not currently shown in the BOQ.</summary>
        public ObservableCollection<BoqColumnViewModel> Available { get; }
            = new ObservableCollection<BoqColumnViewModel>();

        /// <summary>Parameters the user has chosen to show in the BOQ.</summary>
        public ObservableCollection<BoqColumnViewModel> Chosen { get; }
            = new ObservableCollection<BoqColumnViewModel>();

        /// <summary>
        /// New <see cref="BoqColumnViewModel"/> instances created for Revit parameter keys
        /// not previously known to AllColumns.  Caller should add these to AllColumns on OK.
        /// </summary>
        public IReadOnlyList<BoqColumnViewModel> NewColumns =>
            _newColVMs.Values
                      .Where(c => Chosen.Contains(c))
                      .ToList();

        // ── Selection ───────────────────────────────────────────────────────

        private BoqColumnViewModel _selectedAvailable;
        public BoqColumnViewModel SelectedAvailable
        {
            get => _selectedAvailable;
            set => SetField(ref _selectedAvailable, value);
        }

        private BoqColumnViewModel _selectedChosen;
        public BoqColumnViewModel SelectedChosen
        {
            get => _selectedChosen;
            set => SetField(ref _selectedChosen, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ICommand AddCommand    { get; }
        public ICommand RemoveCommand { get; }
        public ICommand OkCommand     { get; }
        public ICommand CancelCommand { get; }

        // ── Callbacks ─────────────────────────────────────────────────────────

        /// <summary>Raised when the user clicks OK. Subscriber should rebuild the DataGrid.</summary>
        public event EventHandler OnApplied;

        private Action _closeAction;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <param name="revitParameterNames">
        ///     All parameter names discovered by scanning the Revit category.
        ///     May be empty (falls back to showing only already-known columns).
        /// </param>
        /// <param name="existingColumns">
        ///     All non-custom <see cref="BoqColumnViewModel"/> entries from AllColumns.
        ///     Visible ones go to Chosen; hidden ones may appear in Available.
        /// </param>
        public BoqParameterPickerViewModel(
            IReadOnlyList<string> revitParameterNames,
            IEnumerable<BoqColumnViewModel> existingColumns)
        {
            // Build lookup and snapshot original visibility
            foreach (var col in existingColumns.Where(c => !c.IsCustom))
            {
                _existingCols[col.FieldKey]         = col;
                _originalVisibility[col.FieldKey]   = col.IsVisible;
            }

            // Chosen = existing columns that are currently visible
            foreach (var col in _existingCols.Values
                                             .Where(c => c.IsVisible)
                                             .OrderBy(c => c.Header, StringComparer.OrdinalIgnoreCase))
                Chosen.Add(col);

            // Available = all Revit keys not already Chosen
            var chosenKeys = new HashSet<string>(Chosen.Select(c => c.FieldKey), StringComparer.OrdinalIgnoreCase);

            foreach (var key in revitParameterNames ?? Array.Empty<string>())
            {
                if (chosenKeys.Contains(key)) continue;

                if (_existingCols.TryGetValue(key, out var existing))
                {
                    InsertSorted(Available, existing);
                }
                else
                {
                    // Brand-new key — create a provisional BoqColumnViewModel
                    var def = new BoqColumnDefinition(key, key, isVisible: false) { IsDiscovered = true };
                    var newVm = new BoqColumnViewModel(def);
                    _newColVMs[key] = newVm;
                    InsertSorted(Available, newVm);
                }
            }

            // Also include hidden existing columns not covered by the Revit scan
            foreach (var col in _existingCols.Values.Where(c => !c.IsVisible))
                if (!Available.Contains(col))
                    InsertSorted(Available, col);

            AddCommand    = new RelayCommand(ExecuteAdd,    () => SelectedAvailable != null);
            RemoveCommand = new RelayCommand(ExecuteRemove, () => SelectedChosen    != null);
            OkCommand     = new RelayCommand(ExecuteOk);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        // ── Command handlers ──────────────────────────────────────────────────

        private void ExecuteAdd()
        {
            var col = SelectedAvailable;
            if (col == null) return;

            Available.Remove(col);
            col.IsVisible = true;
            InsertSorted(Chosen, col);

            SelectedAvailable = null;
            SelectedChosen    = col;
        }

        private void ExecuteRemove()
        {
            var col = SelectedChosen;
            if (col == null) return;

            Chosen.Remove(col);
            col.IsVisible = false;
            InsertSorted(Available, col);

            SelectedChosen    = null;
            SelectedAvailable = col;
        }

        private void ExecuteOk()
        {
            OnApplied?.Invoke(this, EventArgs.Empty);
            _closeAction?.Invoke();
        }

        private void ExecuteCancel()
        {
            // Restore original visibility on all existing columns
            foreach (var kvp in _originalVisibility)
                if (_existingCols.TryGetValue(kvp.Key, out var col))
                    col.IsVisible = kvp.Value;

            _closeAction?.Invoke();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Supplies the delegate the window uses to close itself.</summary>
        public void SetCloseAction(Action closeAction) => _closeAction = closeAction;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void InsertSorted(ObservableCollection<BoqColumnViewModel> list, BoqColumnViewModel item)
        {
            int idx = list.TakeWhile(c => string.Compare(c.Header, item.Header, StringComparison.OrdinalIgnoreCase) < 0).Count();
            list.Insert(idx, item);
        }
    }
}
