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
            viewModel.EditSymbolRequested  += OpenSymbolDesigner;
        }

        private void OpenSymbolDesigner()
        {
            OpenSymbolDesigner(null);
        }

        private void OpenSymbolDesigner(CustomSymbolDefinition existing)
        {
            var designerVm = new SymbolDesignerViewModel();

            // Pre-load when editing an existing symbol
            if (existing != null)
                designerVm.LoadFrom(existing);

            designerVm.Saved += definition =>
            {
                if (existing != null)
                {
                    // Replace the old definition in-place so name/snap-origin changes propagate
                    _vm.ReplaceSymbolInLibrary(existing, definition);
                }
                else
                {
                    // Brand-new symbol: just add to dropdown
                    _vm.AddSymbolToLibrary(definition);
                }

                // Notify the owner (MainViewModel) so it can persist the library
                SymbolCreated?.Invoke(definition);
            };

            var win = new SymbolDesignerWindow(designerVm) { Owner = this };
            win.ShowDialog();
        }
    }
}
