using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// ViewModel for the parameter picker dialog.
    ///
    /// Splits all non-custom <see cref="BoqColumnViewModel"/> entries into two buckets:
    ///   • Available — discovered / standard columns that are currently hidden
    ///   • Chosen    — columns currently visible in the BOQ DataGrid
    ///
    /// Moving items between buckets immediately sets <see cref="BoqColumnViewModel.IsVisible"/>.
    /// The caller is notified via <see cref="OnApplied"/> when the user confirms.
    /// </summary>
    public class BoqParameterPickerViewModel : ViewModelBase
    {
        // ── Collections ───────────────────────────────────────────────────────

        /// <summary>Parameters known to Revit but not currently shown in the BOQ.</summary>
        public ObservableCollection<BoqColumnViewModel> Available { get; }
            = new ObservableCollection<BoqColumnViewModel>();

        /// <summary>Parameters the user has chosen to show in the BOQ.</summary>
        public ObservableCollection<BoqColumnViewModel> Chosen { get; }
            = new ObservableCollection<BoqColumnViewModel>();

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

        /// <param name="allColumns">All non-custom column view-models from the BOQ window.
        ///     Columns where <see cref="BoqColumnViewModel.IsVisible"/> is false go to Available;
        ///     visible ones go to Chosen.</param>
        public BoqParameterPickerViewModel(System.Collections.Generic.IEnumerable<BoqColumnViewModel> allColumns)
        {
            // Split into available / chosen, sorted alphabetically within each list
            foreach (var col in allColumns.Where(c => !c.IsCustom).OrderBy(c => c.Header))
            {
                if (col.IsVisible)
                    Chosen.Add(col);
                else
                    Available.Add(col);
            }

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

            // Insert into Chosen maintaining alphabetical order
            int idx = Chosen.TakeWhile(c => string.Compare(c.Header, col.Header, StringComparison.OrdinalIgnoreCase) < 0).Count();
            Chosen.Insert(idx, col);

            SelectedAvailable = null;
            SelectedChosen    = col;
        }

        private void ExecuteRemove()
        {
            var col = SelectedChosen;
            if (col == null) return;

            Chosen.Remove(col);
            col.IsVisible = false;

            // Insert back into Available in sorted position
            int idx = Available.TakeWhile(c => string.Compare(c.Header, col.Header, StringComparison.OrdinalIgnoreCase) < 0).Count();
            Available.Insert(idx, col);

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
            // Revert any IsVisible changes made during this session
            foreach (var col in Available)
                col.IsVisible = false;
            foreach (var col in Chosen)
                col.IsVisible = true;

            _closeAction?.Invoke();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Supplies the delegate the window uses to close itself.</summary>
        public void SetCloseAction(Action closeAction) => _closeAction = closeAction;
    }
}
