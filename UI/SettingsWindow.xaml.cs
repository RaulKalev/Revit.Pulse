using System.Windows;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.SettingsSaved += _ => Close();
            viewModel.Cancelled += Close;
        }
    }
}
