using Pulse.Core.Boq;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// UI wrapper around <see cref="BoqColumnDefinition"/> for the column chooser panel.
    /// Raises PropertyChanged so the DataGrid rebuilds when visibility is toggled.
    /// </summary>
    public class BoqColumnViewModel : ViewModelBase
    {
        private readonly BoqColumnDefinition _definition;

        public string FieldKey  => _definition.FieldKey;
        public string Header    => _definition.Header;
        public bool   IsCustom  => _definition.IsCustom;

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (SetField(ref _isVisible, value))
                    _definition.IsVisible = value;
            }
        }

        private int _displayOrder;
        public int DisplayOrder
        {
            get => _displayOrder;
            set
            {
                if (SetField(ref _displayOrder, value))
                    _definition.DisplayOrder = value;
            }
        }

        public BoqColumnViewModel(BoqColumnDefinition definition)
        {
            _definition = definition;
            _isVisible  = definition.IsVisible;
            _displayOrder = definition.DisplayOrder;
        }

        /// <summary>Returns the underlying model updated with the latest UI values.</summary>
        public BoqColumnDefinition ToDefinition()
        {
            _definition.IsVisible    = _isVisible;
            _definition.DisplayOrder = _displayOrder;
            return _definition;
        }
    }
}
