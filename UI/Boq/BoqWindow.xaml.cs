using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Pulse.Helpers;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// Modeless BOQ window code-behind.
    ///
    /// Responsibilities (beyond what MVVM handles):
    ///   • Build / rebuild DataGrid columns when ViewModel raises <see cref="BoqWindowViewModel.ColumnsChanged"/>.
    ///   • Save settings on window close.
    ///   • Apply GroupStyle so grouped rows render correctly.
    ///
    /// All business logic stays in <see cref="BoqWindowViewModel"/>.
    /// </summary>
    public partial class BoqWindow : Window
    {
        private readonly BoqWindowViewModel _vm;
        private readonly WindowResizer _resizer;

        public BoqWindow(BoqWindowViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _vm;

            _resizer = new WindowResizer(this);

            // Re-build DataGrid columns whenever the ViewModel says the layout changed.
            _vm.ColumnsChanged += (_, __) => RebuildColumns();

            Loaded  += OnLoaded;
            Closing += OnClosing;
        }

        // ── Window events ─────────────────────────────────────────────────────

        private const string PlacementFile = "boq_window_placement.json";

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Restore saved size/position, or centre near the screen centre at a sensible default.
            var p = WindowPlacementService.Load(PlacementFile);
            if (p != null)
            {
                Left   = p.Left;
                Top    = p.Top;
                Width  = Math.Max(p.Width,  600);
                Height = Math.Max(p.Height, 400);
            }
            else
            {
                Width  = 900;
                Height = 600;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Build initial columns now that the ViewModel has been initialised.
            RebuildColumns();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Persist position + size.
            WindowPlacementService.Save(Left, Top, Width, Height, PlacementFile);

            // Auto-save settings on window close.
            _vm.SaveSettingsCommand?.Execute(null);
        }

        // ── DataGrid column builder ───────────────────────────────────────────

        /// <summary>
        /// Clears all existing DataGrid columns and adds one DataGridTextColumn per
        /// visible column returned by the ViewModel in display order.
        ///
        /// Binding uses the indexer path "[FieldKey]" which resolves against
        /// <see cref="BoqRowViewModel"/>'s string indexer.
        /// </summary>
        private void RebuildColumns()
        {
            if (TheDataGrid == null) return;

            TheDataGrid.Columns.Clear();

            var visible = _vm.GetVisibleColumnsOrdered();
            int displayIndex = 0;

            foreach (var col in visible)
            {
                var column = new DataGridTextColumn
                {
                    Header       = col.Header,
                    Binding      = new Binding($"[{col.FieldKey}]")
                    {
                        Mode = BindingMode.OneWay
                    },
                    IsReadOnly   = true,
                    DisplayIndex = displayIndex++,
                    MinWidth     = 60,
                    Width        = DataGridLength.Auto,
                };

                TheDataGrid.Columns.Add(column);
            }
        }
        // ── Resize grip handlers ─────────────────────────────────────────────

        private void ResizeLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Left);

        private void ResizeRight_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Right);

        private void ResizeBottom_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Bottom);

        private void ResizeBottomLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.BottomLeft);

        private void ResizeBottomRight_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.BottomRight);

        private void Window_MouseMove(object sender, MouseEventArgs e)
            => _resizer.ResizeWindow(e);

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
            => _resizer.StopResizing();
    }
}
