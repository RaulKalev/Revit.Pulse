using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace Pulse.UI.Controls
{
    public partial class DiagramPanel : UserControl
    {
        private const double ExpandedWidth  = 300;
        private const double CollapsedWidth = 32;

        private bool _isExpanded = true;

        public DiagramPanel()
        {
            InitializeComponent();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
            ApplyState();
        }

        private void ApplyState()
        {
            if (_isExpanded)
            {
                Width                        = ExpandedWidth;
                DiagramContent.Visibility    = Visibility.Visible;
                HeaderTitleStack.Visibility  = Visibility.Visible;
                CollapsedLabel.Visibility    = Visibility.Collapsed;
                ToggleIcon.Kind              = PackIconKind.ChevronRight;
            }
            else
            {
                Width                        = CollapsedWidth;
                DiagramContent.Visibility    = Visibility.Collapsed;
                HeaderTitleStack.Visibility  = Visibility.Collapsed;
                CollapsedLabel.Visibility    = Visibility.Visible;
                ToggleIcon.Kind              = PackIconKind.ChevronLeft;
            }
        }
    }
}
