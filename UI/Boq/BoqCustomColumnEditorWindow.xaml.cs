using System.Windows;
using System.Windows.Input;
using Pulse.Core.Boq;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// Code-behind for the Add/Edit custom column modal dialog.
    /// Uses thin code-behind pattern: populates controls from the ViewModel
    /// and reads back on OK.  All validation lives in the ViewModel.
    /// </summary>
    public partial class BoqCustomColumnEditorWindow : Window
    {
        private readonly BoqCustomColumnEditorViewModel _vm;

        public BoqCustomColumnEditorWindow(BoqCustomColumnEditorViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;

            // Populate Kind combobox
            KindBox.Items.Add("Concat (string concatenation)");
            KindBox.Items.Add("Sum (numeric)");
            KindBox.Items.Add("JoinDelimited (skip empty)");
            KindBox.SelectedIndex = (int)_vm.Kind;

            // Populate source lists
            foreach (var k in _vm.AvailableSourceKeys)
                AvailableList.Items.Add(k);

            foreach (var k in _vm.SelectedSourceKeys)
                SelectedList.Items.Add(k);

            // Init fields
            HeaderBox.Text    = _vm.Header;
            DelimiterBox.Text = _vm.Delimiter;

            UpdateDelimiterVisibility();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void KindBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.Kind = (CustomColumnKind)KindBox.SelectedIndex;
            UpdateDelimiterVisibility();
        }

        private void UpdateDelimiterVisibility()
        {
            bool needsDelimiter = _vm.Kind != CustomColumnKind.Sum;
            DelimiterLabel.Visibility = needsDelimiter ? Visibility.Visible : Visibility.Collapsed;
            DelimiterBox.Visibility   = needsDelimiter ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddSource_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableList.SelectedItem is string key)
                MoveToSelected(key);
        }

        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedList.SelectedItem is string key)
                MoveToAvailable(key);
        }

        private void AvailableList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AvailableList.SelectedItem is string key)
                MoveToSelected(key);
        }

        private void SelectedList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedList.SelectedItem is string key)
                MoveToAvailable(key);
        }

        private void MoveToSelected(string key)
        {
            if (SelectedList.Items.Contains(key)) return;
            SelectedList.Items.Add(key);
        }

        private void MoveToAvailable(string key)
        {
            SelectedList.Items.Remove(key);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Flush UI → ViewModel
            _vm.Header    = HeaderBox.Text.Trim();
            _vm.Delimiter = DelimiterBox.Text;
            _vm.Kind      = (CustomColumnKind)KindBox.SelectedIndex;

            _vm.SelectedSourceKeys.Clear();
            foreach (string key in SelectedList.Items)
                _vm.SelectedSourceKeys.Add(key);

            if (!_vm.IsValid)
            {
                MessageBox.Show(
                    "Please enter a column header and select at least one source field.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
