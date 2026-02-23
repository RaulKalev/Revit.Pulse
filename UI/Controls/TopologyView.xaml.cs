using System.Windows.Controls;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Controls
{
    /// <summary>
    /// Displays the system topology as a hierarchical tree.
    /// Panel -> Loop -> Device structure.
    /// </summary>
    public partial class TopologyView : UserControl
    {
        public TopologyView()
        {
            InitializeComponent();
        }

        private void TopologyTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is TopologyViewModel vm && e.NewValue is TopologyNodeViewModel node)
            {
                vm.SelectedNode = node;
            }
        }
    }
}
