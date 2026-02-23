using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pulse.Core.Graph;
using Pulse.Core.Modules;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the topology tree view.
    /// Displays the hierarchical Panel -> Loop -> Device structure.
    /// </summary>
    public class TopologyViewModel : ViewModelBase
    {
        /// <summary>
        /// Root-level nodes displayed in the TreeView (typically panels).
        /// </summary>
        public ObservableCollection<TopologyNodeViewModel> RootNodes { get; } = new ObservableCollection<TopologyNodeViewModel>();

        /// <summary>
        /// All nodes (flat list for searching).
        /// </summary>
        private List<TopologyNodeViewModel> _allNodes = new List<TopologyNodeViewModel>();

        /// <summary>
        /// Fired when a node is selected in the topology.
        /// </summary>
        public event Action<Node> NodeSelected;

        private TopologyNodeViewModel _selectedNode;
        public TopologyNodeViewModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetField(ref _selectedNode, value))
                {
                    NodeSelected?.Invoke(value?.GraphNode);
                }
            }
        }

        /// <summary>
        /// Load the topology from collected module data.
        /// Builds a tree of TopologyNodeViewModel instances.
        /// </summary>
        public void LoadFromModuleData(ModuleData data)
        {
            RootNodes.Clear();
            _allNodes.Clear();

            if (data == null) return;

            // Build lookup of nodes by id
            var nodeMap = new Dictionary<string, Node>();
            foreach (var node in data.Nodes)
            {
                nodeMap[node.Id] = node;
            }

            // Build adjacency list (parent -> children)
            var children = new Dictionary<string, List<string>>();
            var hasParent = new HashSet<string>();

            foreach (var edge in data.Edges)
            {
                if (!children.TryGetValue(edge.SourceId, out var list))
                {
                    list = new List<string>();
                    children[edge.SourceId] = list;
                }
                list.Add(edge.TargetId);
                hasParent.Add(edge.TargetId);
            }

            // Root nodes are those without a parent
            var rootIds = data.Nodes
                .Where(n => !hasParent.Contains(n.Id))
                .Select(n => n.Id)
                .ToList();

            // Build the tree recursively
            foreach (string rootId in rootIds)
            {
                if (nodeMap.TryGetValue(rootId, out var rootNode))
                {
                    var vm = BuildNodeTree(rootNode, nodeMap, children, data);
                    RootNodes.Add(vm);
                }
            }
        }

        /// <summary>
        /// Recursively build the tree of TopologyNodeViewModel instances.
        /// </summary>
        private TopologyNodeViewModel BuildNodeTree(
            Node node,
            Dictionary<string, Node> nodeMap,
            Dictionary<string, List<string>> children,
            ModuleData data)
        {
            var vm = new TopologyNodeViewModel(node);

            // Count warnings for this entity
            int warningCount = data.RuleResults.Count(r => r.EntityId == node.Id && r.Severity >= Core.Rules.Severity.Warning);
            vm.WarningCount = warningCount;

            // Add child count info for loops
            if (node.NodeType == "Loop" && node.Properties.TryGetValue("DeviceCount", out string dc))
            {
                vm.SubInfo = $"({dc} devices)";
            }

            _allNodes.Add(vm);

            if (children.TryGetValue(node.Id, out var childIds))
            {
                // Collect child nodes, sort numerically by type-appropriate key,
                // then build their sub-trees.
                var childNodes = new List<(string id, Node node, int sortKey)>();
                foreach (string childId in childIds)
                {
                    if (nodeMap.TryGetValue(childId, out var childNode))
                    {
                        int key = GetNumericSortKey(childNode);
                        childNodes.Add((childId, childNode, key));
                    }
                }

                childNodes.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

                foreach (var (_, childNode, _) in childNodes)
                {
                    var childVm = BuildNodeTree(childNode, nodeMap, children, data);
                    vm.Children.Add(childVm);
                }
            }

            return vm;
        }

        /// <summary>
        /// Filter the displayed topology to only show nodes in the specified set (and their ancestors).
        /// </summary>
        public void FilterToEntities(HashSet<string> entityIds)
        {
            foreach (var node in _allNodes)
            {
                bool visible = entityIds.Contains(node.GraphNode.Id) || HasVisibleDescendant(node, entityIds);
                node.IsVisible = visible;
            }
        }

        /// <summary>
        /// Clear any active filter — show all nodes.
        /// </summary>
        public void ClearFilter()
        {
            foreach (var node in _allNodes)
            {
                node.IsVisible = true;
            }
        }

        private bool HasVisibleDescendant(TopologyNodeViewModel vm, HashSet<string> entityIds)
        {
            foreach (var child in vm.Children)
            {
                if (entityIds.Contains(child.GraphNode.Id) || HasVisibleDescendant(child, entityIds))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns a numeric sort key for a node.
        /// Loops:   parse the Loop number from the label ("Loop 3" → 3).
        /// Devices:  parse the Address property as an integer.
        /// Others:  fall back to parsing the label.
        /// Strings that contain no leading integer sort after all numeric values.
        /// </summary>
        private static int GetNumericSortKey(Node node)
        {
            if (node.NodeType == "Device")
            {
                if (node.Properties.TryGetValue("Address", out string addr) && !string.IsNullOrWhiteSpace(addr))
                    return ParseLeadingInt(addr);
                // Fall back to label when address is absent
                return ParseLeadingInt(node.Label);
            }

            // Loop, Panel, Zone — sort by the first integer found in the label
            return ParseLeadingInt(node.Label);
        }

        /// <summary>
        /// Extracts the first contiguous run of digits from a string and returns
        /// it as an integer.  Returns int.MaxValue when no digit is found so that
        /// label-less items always sort to the end.
        /// </summary>
        private static int ParseLeadingInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return int.MaxValue;

            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    // First run of digits ended
                    if (int.TryParse(text.Substring(start, i - start), out int v)) return v;
                    start = -1;
                }
            }

            // Digits ran to the end of the string
            if (start >= 0 && int.TryParse(text.Substring(start), out int val)) return val;

            return int.MaxValue;
        }
    }

    /// <summary>
    /// ViewModel for a single node in the topology tree.
    /// </summary>
    public class TopologyNodeViewModel : ViewModelBase
    {
        public Node GraphNode { get; }

        public string Label => GraphNode.Label;
        public string NodeType => GraphNode.NodeType;

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set => SetField(ref _warningCount, value);
        }

        private string _subInfo;
        public string SubInfo
        {
            get => _subInfo;
            set => SetField(ref _subInfo, value);
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetField(ref _isVisible, value);
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetField(ref _isExpanded, value);
        }

        public ObservableCollection<TopologyNodeViewModel> Children { get; } = new ObservableCollection<TopologyNodeViewModel>();

        public TopologyNodeViewModel(Node graphNode)
        {
            GraphNode = graphNode ?? throw new ArgumentNullException(nameof(graphNode));
        }

        /// <summary>Returns a display icon path or character based on node type.</summary>
        public string Icon
        {
            get
            {
                switch (NodeType)
                {
                    case "Panel": return "ServerNetwork";
                    case "Loop": return "VectorPolyline";
                    case "Zone": return "MapMarkerRadius";
                    case "Device": return "AccessPoint";
                    default: return "CircleOutline";
                }
            }
        }
    }
}
