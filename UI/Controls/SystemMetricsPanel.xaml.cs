using System;
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
        /// <summary>Fired when the panel is expanded or collapsed. Parameter = new expanded state.</summary>
        public event Action<bool> ExpandedChanged;

        public SystemMetricsPanel()
        {
            InitializeComponent();

            ExpandToggle.Checked   += (s, e) => ExpandedChanged?.Invoke(true);
            ExpandToggle.Unchecked += (s, e) => ExpandedChanged?.Invoke(false);
        }

        /// <summary>Gets or sets whether the content area is expanded.</summary>
        public bool IsExpanded
        {
            get => ExpandToggle.IsChecked == true;
            set => ExpandToggle.IsChecked = value;
        }
    }
}
