using System.Windows;
using System.Windows.Input;
using Pulse.Helpers;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// Code-behind for the BOQ parameter picker dialog.
    /// Business logic lives entirely in <see cref="BoqParameterPickerViewModel"/>.
    /// </summary>
    public partial class BoqParameterPickerWindow : Window
    {
        private readonly BoqParameterPickerViewModel _vm;
        private readonly WindowResizer _resizer;

        public BoqParameterPickerWindow(BoqParameterPickerViewModel viewModel)
        {
            InitializeComponent();

            _vm         = viewModel;
            DataContext = _vm;
            _resizer    = new WindowResizer(this);

            // Let the VM close this window
            _vm.SetCloseAction(() => Close());
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
