using System;
using System.Windows;
using Pulse.Core.Settings;
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
        private readonly SymbolMappingViewModel _vm;

        /// <summary>
        /// Raised when the user creates a new symbol via the designer.
        /// The owner (MainViewModel) should save the library on this event.
        /// </summary>
        public event Action<CustomSymbolDefinition> SymbolCreated;

        public SymbolMappingWindow(SymbolMappingViewModel viewModel)
        {
            InitializeComponent();

            _vm = viewModel;
            DataContext = viewModel;

            viewModel.Saved                += _ => Close();
            viewModel.Cancelled            += Close;
            viewModel.NewSymbolRequested   += OpenSymbolDesigner;
        }

        private void OpenSymbolDesigner()
        {
            var designerVm = new SymbolDesignerViewModel();

            designerVm.Saved += definition =>
            {
                // Add to mapping dropdown immediately
                _vm.AddSymbolToLibrary(definition);

                // Notify owner so it can persist the library
                SymbolCreated?.Invoke(definition);
            };

            var win = new SymbolDesignerWindow(designerVm) { Owner = this };
            win.ShowDialog();
        }
    }
}
