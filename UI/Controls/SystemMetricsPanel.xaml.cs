using System.Windows.Controls;

namespace Pulse.UI.Controls
{
    /// <summary>
    /// Collapsible panel that displays system-level metrics (capacity gauges)
    /// for the currently selected Panel or Loop node.
    /// Collapsed by default; expands when the user clicks the header.
    /// </summary>
    public partial class SystemMetricsPanel : UserControl
    {
        public SystemMetricsPanel()
        {
            InitializeComponent();
        }
    }
}
