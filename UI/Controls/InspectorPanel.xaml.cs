using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Controls
{
    /// <summary>
    /// Inspector panel showing details of the currently selected topology node.
    /// Displays properties, warning count, device count, and individual warnings.
    /// </summary>
    public partial class InspectorPanel : UserControl
    {
        public InspectorPanel()
        {
            InitializeComponent();
        }

        private InspectorViewModel Vm => DataContext as InspectorViewModel;

        // ── Current Draw Normal ───────────────────────────────────────────

        private void CurrentDrawNormalDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { Vm?.BeginEditNormal(); e.Handled = true; }
        }

        private void CurrentDrawNormalTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { Vm?.CommitNormal(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Vm?.CancelEdit(); e.Handled = true; }
        }

        private void CurrentDrawNormalTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Vm?.CommitNormal();
        }

        private void CurrentDrawNormalTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && sender is TextBox tb) { tb.Focus(); tb.SelectAll(); }
        }

        // ── Current Draw Alarm ────────────────────────────────────────────

        private void CurrentDrawAlarmDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { Vm?.BeginEditAlarm(); e.Handled = true; }
        }

        private void CurrentDrawAlarmTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  { Vm?.CommitAlarm(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Vm?.CancelEdit(); e.Handled = true; }
        }

        private void CurrentDrawAlarmTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Vm?.CommitAlarm();
        }

        private void CurrentDrawAlarmTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && sender is TextBox tb) { tb.Focus(); tb.SelectAll(); }
        }
    }
}
