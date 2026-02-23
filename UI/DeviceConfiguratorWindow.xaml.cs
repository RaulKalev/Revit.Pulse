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
    }
}
