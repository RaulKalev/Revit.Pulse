using System.Windows.Controls;
using Pulse.UI.ViewModels;

namespace Pulse.UI.Controls
{
    /// <summary>
    /// Displays the system topology as nested collapsible cards.
    /// Panel → Loop → Device hierarchy.
    /// </summary>
    public partial class TopologyView : UserControl
    {
        public TopologyView()
        {
            InitializeComponent();
        }
    }
}
