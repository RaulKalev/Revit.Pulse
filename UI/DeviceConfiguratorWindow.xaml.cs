using System.Windows;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    /// <summary>
    /// Code-behind for the Device Configurator window.
    /// Hosts the <see cref="DeviceConfigViewModel"/> which manages control panels
    /// and loop modules persisted to a local JSON file.
    /// </summary>
    public partial class DeviceConfiguratorWindow : Window
    {
        public DeviceConfiguratorWindow(DeviceConfigViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.Saved     += Close;
            viewModel.Cancelled += Close;
        }

        private void ColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as DeviceConfigViewModel;
            if (vm?.SelectedWire == null) return;

            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                var current = vm.SelectedWire.Color;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    try
                    {
                        var wpf = (System.Windows.Media.Color)
                            System.Windows.Media.ColorConverter.ConvertFromString(current);
                        dialog.Color = System.Drawing.Color.FromArgb(wpf.R, wpf.G, wpf.B);
                    }
                    catch { /* ignore invalid hex */ }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = dialog.Color;
                    vm.SelectedWire.Color = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                }
            }
        }
    }
}
