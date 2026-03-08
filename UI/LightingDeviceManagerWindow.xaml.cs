using System.Windows;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    /// <summary>
    /// Editor window for the DALI hardware device catalog.
    /// Hosts <see cref="LightingDeviceManagerViewModel"/> which manages
    /// controllers and power supplies stored in %APPDATA%\Pulse\lighting-devices.json.
    /// </summary>
    public partial class LightingDeviceManagerWindow : Window
    {
        public LightingDeviceManagerWindow(LightingDeviceManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.Saved     += Close;
            viewModel.Cancelled += Close;
        }
    }
}
