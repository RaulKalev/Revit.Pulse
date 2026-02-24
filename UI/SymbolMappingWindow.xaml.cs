using System.Windows;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    /// <summary>
    /// Code-behind for the Symbol Mapping window.
    /// Hosts <see cref="SymbolMappingViewModel"/> which lists all devices in the current
    /// Revit document and lets the user assign a symbol key to each one.
    /// </summary>
    public partial class SymbolMappingWindow : Window
    {
        public SymbolMappingWindow(SymbolMappingViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.Saved     += _ => Close();
            viewModel.Cancelled += Close;
        }
    }
}
