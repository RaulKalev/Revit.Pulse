using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Pulse.Core.Graph;
using Pulse.Core.Graph.Canvas;
using Pulse.Core.Modules;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the topology tree view.
    /// Displays the hierarchical Panel -> Loop -> Device structure.
    ///
    /// Internally holds a <see cref="CanvasGraphModel"/> built from <see cref="ModuleData"/>
    /// during each refresh.  The TreeView is a projection of this internal model.
    /// A future visual canvas can consume the same <see cref="CanvasGraph"/> directly.
    /// </summary>
    public class TopologyViewModel : ViewModelBase
    {
        /// <summary>
        /// Internal canvas graph model rebuilt on every refresh.
        /// Consumed by the TreeView projection (this class) and, in the future,
        /// by a visual canvas renderer.
        /// </summary>
        public CanvasGraphModel CanvasGraph { get; private set; } = CanvasGraphModel.Empty;

        /// <summary>
        /// Root-level nodes displayed in the TreeView (typically panels).
        /// </summary>
        public ObservableCollection<TopologyNodeViewModel> RootNodes { get; } = new ObservableCollection<TopologyNodeViewModel>();

        /// <summary>
        /// All nodes (flat list for searching).
        /// </summary>
        private List<TopologyNodeViewModel> _allNodes = new List<TopologyNodeViewModel>();

        /// <summary>
        /// Node IDs that must be expanded on the next <see cref="RestoreExpandState"/> call,
        /// regardless of whether they were in the snapshot.
        /// Set by MainViewModel before triggering a refresh (e.g. after assigning a sub-device).
        /// Consumed and cleared by <see cref="RestoreExpandState"/>.
        /// </summary>
        private readonly HashSet<string> _scheduledExpandIds = new HashSet<string>();

        /// <summary>Marks a node ID to be force-expanded on the next RestoreExpandState call.</summary>
        public void ScheduleExpand(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId))
                _scheduledExpandIds.Add(nodeId);
        }

        /// <summary>Current device config store — kept in sync after each load (library data only).</summary>
        private DeviceConfigStore _deviceStore = new DeviceConfigStore();

        /// <summary>Current topology assignments store — per-document, from Extensible Storage.</summary>
        private TopologyAssignmentsStore _assignmentsStore = new TopologyAssignmentsStore();

        /// <summary>
        /// Raised after any assignment mutation so that MainViewModel can persist
        /// the updated store to Revit Extensible Storage via an ExternalEvent.
        /// </summary>
        public Action AssignmentsSaveRequested { get; set; }

        /// <summary>Apply topology assignments loaded from Extensible Storage.
        /// Must be called before <see cref="LoadFromModuleData"/> so initial combobox values are correct.
        /// </summary>
        public void LoadAssignments(TopologyAssignmentsStore store)
        {
            _assignmentsStore = store ?? new TopologyAssignmentsStore();
        }

        /// <summary>
        /// Fired when a node is selected in the topology.
        /// </summary>
        public event Action<Node> NodeSelected;

        /// <summary>
        /// Fired when the user picks a config in a panel/loop combobox.
        /// MainViewModel handles the ExternalEvent write.
        /// </summary>
        public event Action<TopologyNodeViewModel> ConfigAssigned;

        /// <summary>
        /// Fired when the user picks a wire type in a loop wire combobox.
        /// MainViewModel handles the ExternalEvent write.
        /// </summary>
        public event Action<TopologyNodeViewModel> WireAssigned;

        /// <summary>
        /// Fired when the user selects an unassigned device to attach beneath a host device.
        /// Carries: (hostDeviceVm, selectedOption).
        /// MainViewModel writes Loop + Address to Revit and calls <see cref="TopologyNodeViewModel.MarkSubDeviceAdded"/> on success.
        /// </summary>
        public event Action<TopologyNodeViewModel, UnassignedDeviceOption> SubDeviceAssignRequested;
        /// <summary>
        /// Fired when the user clicks "Pick from Revit" on a device row.
        /// MainViewModel minimises the window, opens the Revit pick session, then writes
        /// Loop + Address when an element is selected.
        /// </summary>
        public event Action<TopologyNodeViewModel> PickElementForDeviceRequested;
        /// <summary>
        /// Returns the Loop node whose parent matches <paramref name="panelName"/> and
        /// label matches <paramref name="loopName"/>, or null if not found.
        /// </summary>
        public TopologyNodeViewModel FindLoopNode(string panelName, string loopName)
        {
            return _allNodes.FirstOrDefault(n =>
                n.NodeType == "Loop" &&
                string.Equals(n.Label, loopName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(n.ParentLabel, panelName, StringComparison.OrdinalIgnoreCase));
        }

        private TopologyNodeViewModel _selectedNode;
        public TopologyNodeViewModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                var prev = _selectedNode;
                if (SetField(ref _selectedNode, value))
                {
                    if (prev != null) prev.IsSelected = false;
                    if (value != null) value.IsSelected = true;
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

            // Rebuild internal canvas graph model (consumed by future canvas renderer)
            CanvasGraph = CanvasGraphBuilder.Build(data);

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

            Action<TopologyNodeViewModel> onSelect = node => SelectedNode = node;

            // Load device config for combobox options and initial assignments
            _deviceStore = DeviceConfigService.Load();
            var panelOptions = _deviceStore.ControlPanels.Select(p => p.Name).ToList();
            var loopOptions  = _deviceStore.LoopModules.Select(m => m.Name).ToList();
            var wireOptions  = _deviceStore.Wires.Select(w => w.Name).ToList();

            Action<TopologyNodeViewModel> onAssignConfig = vm =>
            {
                // Persist assignment to per-document store
                if (vm.NodeType == "Panel")
                    _assignmentsStore.PanelAssignments[vm.Label] = vm.AssignedConfig ?? string.Empty;
                else if (vm.NodeType == "Loop")
                    _assignmentsStore.LoopAssignments[vm.Label] = vm.AssignedConfig ?? string.Empty;
                AssignmentsSaveRequested?.Invoke();

                // Notify MainViewModel so it can write to Revit
                ConfigAssigned?.Invoke(vm);
            };

            Action<TopologyNodeViewModel> onAssignWire = vm =>
            {
                // vm.ParentLabel + "::" + vm.Label is the key used by DiagramViewModel
                string key = (vm.ParentLabel ?? string.Empty) + "::" + vm.Label;
                if (string.IsNullOrEmpty(vm.AssignedWire))
                    _assignmentsStore.LoopWireAssignments.Remove(key);
                else
                    _assignmentsStore.LoopWireAssignments[key] = vm.AssignedWire;
                AssignmentsSaveRequested?.Invoke();

                WireAssigned?.Invoke(vm);
            };

            // Build unassigned device pool (devices not connected to any loop)
            var unassignedOptions = data.Devices
                .Where(d => string.IsNullOrEmpty(d.LoopId) && d.RevitElementId.HasValue)
                .Select(d => new UnassignedDeviceOption(d.DisplayName, d.RevitElementId.Value))
                .ToList();

            Action<TopologyNodeViewModel, UnassignedDeviceOption> onSubDeviceAssign = (vm, option) =>
                SubDeviceAssignRequested?.Invoke(vm, option);

            Action<TopologyNodeViewModel> onPickElementForDevice = vm =>
                PickElementForDeviceRequested?.Invoke(vm);

            // Build the tree recursively
            foreach (string rootId in rootIds)
            {
                if (nodeMap.TryGetValue(rootId, out var rootNode))
                {
                    var vm = BuildNodeTree(rootNode, nodeMap, children, data, onSelect,
                                          onAssignConfig, onAssignWire,
                                          panelOptions, loopOptions, wireOptions,
                                          parentLabel: null,
                                          unassignedOptions: unassignedOptions,
                                          onSubDeviceAssign: onSubDeviceAssign,
                                          onPickElementForDevice: onPickElementForDevice);
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
            ModuleData data,
            Action<TopologyNodeViewModel> onSelect,
            Action<TopologyNodeViewModel> onAssignConfig,
            Action<TopologyNodeViewModel> onAssignWire,
            IReadOnlyList<string> panelOptions,
            IReadOnlyList<string> loopOptions,
            IReadOnlyList<string> wireOptions,
            string parentLabel,
            IReadOnlyList<UnassignedDeviceOption> unassignedOptions = null,
            Action<TopologyNodeViewModel, UnassignedDeviceOption> onSubDeviceAssign = null,
            Action<TopologyNodeViewModel> onPickElementForDevice = null)
        {
            // Determine available config options and current assignment for this node type
            IReadOnlyList<string> availableConfigs = null;
            string initialAssignment = null;
            IReadOnlyList<string> availableWires = null;
            string initialWire = null;

            if (node.NodeType == "Panel")
            {
                availableConfigs = panelOptions;
                _assignmentsStore.PanelAssignments.TryGetValue(node.Label, out initialAssignment);
            }
            else if (node.NodeType == "Loop")
            {
                availableConfigs = loopOptions;
                _assignmentsStore.LoopAssignments.TryGetValue(node.Label, out initialAssignment);

                availableWires = wireOptions;
                // Wire key uses "panelName::loopName" to match DiagramViewModel convention
                string wireKey = (parentLabel ?? string.Empty) + "::" + node.Label;
                _assignmentsStore.LoopWireAssignments.TryGetValue(wireKey, out initialWire);
            }

            var vm = new TopologyNodeViewModel(
                node, onSelect, onAssignConfig, availableConfigs, initialAssignment,
                onAssignWire, availableWires, initialWire, parentLabel,
                availableUnassigned:      node.NodeType == "Device" ? unassignedOptions : null,
                onSubDeviceAssign:        node.NodeType == "Device" ? onSubDeviceAssign : null,
                onPickElementForDevice:   node.NodeType == "Device" ? onPickElementForDevice : null);

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
                    var childVm = BuildNodeTree(childNode, nodeMap, children, data, onSelect,
                                               onAssignConfig, onAssignWire,
                                               panelOptions, loopOptions, wireOptions,
                                               parentLabel: node.Label,
                                               unassignedOptions: unassignedOptions,
                                               onSubDeviceAssign: onSubDeviceAssign,
                                               onPickElementForDevice: onPickElementForDevice);
                    vm.Children.Add(childVm);
                }
            }

            // Collect device ElementIds for this node so the write handler knows which elements to stamp
            if (node.NodeType == "Panel" || node.NodeType == "Loop")
                CollectDescendantDeviceIds(vm);

            // Auto-expand device nodes that have sub-device children
            if (node.NodeType == "Device" && vm.Children.Count > 0)
                vm.IsExpanded = true;

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
        /// Populate DescendantDeviceElementIds on a panel/loop node by walking its children.
        /// </summary>
        private static void CollectDescendantDeviceIds(TopologyNodeViewModel vm)
        {
            foreach (var child in vm.Children)
            {
                if (child.NodeType == "Device" && child.GraphNode.RevitElementId.HasValue)
                    vm.DescendantDeviceElementIds.Add(child.GraphNode.RevitElementId.Value);
                else
                    CollectDescendantDeviceIds(child);
            }
        }

        /// <summary>Returns the IDs of all currently expanded nodes (for UI state persistence).</summary>
        public List<string> GetExpandedNodeIds()
            => _allNodes.Where(n => n.IsExpanded).Select(n => n.GraphNode.Id).ToList();

        /// <summary>Restores expand state for any node whose ID is in the provided set.
        /// Device nodes that were auto-expanded by BuildNodeTree (they have children) are
        /// left expanded even if they were not in the saved snapshot.</summary>
        public void RestoreExpandState(IEnumerable<string> ids)
        {
            var idSet = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            // Merge any nodes explicitly scheduled to be expanded (e.g. host device after sub-device assign)
            if (_scheduledExpandIds.Count > 0)
            {
                foreach (var id in _scheduledExpandIds)
                    idSet.Add(id);
                _scheduledExpandIds.Clear();
            }
            foreach (var node in _allNodes)
            {
                if (idSet.Contains(node.GraphNode.Id))
                    node.IsExpanded = true;
                else if (node.NodeType == "Device" && node.Children.Count > 0)
                    node.IsExpanded = true; // keep auto-expand set by BuildNodeTree
                else
                    node.IsExpanded = false;
            }
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

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>A device from the unassigned pool that can be dropped onto a host device.</summary>
    public class UnassignedDeviceOption
    {
        public string Label { get; }
        public long ElementId { get; }
        public UnassignedDeviceOption(string label, long elementId) { Label = label; ElementId = elementId; }
        public override string ToString() => Label;
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

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetField(ref _isExpanded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public ICommand SelectCommand { get; }

        /// <summary>Config names available for this node type (Panel → panel configs, Loop → loop modules).</summary>
        public ObservableCollection<string> AvailableConfigs { get; } = new ObservableCollection<string>();

        /// <summary>Wire type names available (Loop nodes only).</summary>
        public ObservableCollection<string> AvailableWires { get; } = new ObservableCollection<string>();

        /// <summary>RevitElementIds of all leaf Device descendants — used for Revit parameter writes.</summary>
        public List<long> DescendantDeviceElementIds { get; } = new List<long>();

        /// <summary>Label of the direct parent node (e.g. panel name for a loop node).</summary>
        public string ParentLabel { get; }

        private readonly Action<TopologyNodeViewModel> _onAssignConfig;
        private readonly Action<TopologyNodeViewModel> _onAssignWire;
        private readonly Action<TopologyNodeViewModel> _onPickElementForDevice;

        private string _assignedConfig;
        /// <summary>The currently assigned config name. Setting this triggers a save + Revit write.</summary>
        public string AssignedConfig
        {
            get => _assignedConfig;
            set
            {
                if (SetField(ref _assignedConfig, value))
                    _onAssignConfig?.Invoke(this);
            }
        }

        private string _assignedWire;
        /// <summary>The currently assigned wire type name. Setting this triggers save + Revit write.</summary>
        public string AssignedWire
        {
            get => _assignedWire;
            set
            {
                if (SetField(ref _assignedWire, value))
                    _onAssignWire?.Invoke(this);
            }
        }

        /// <summary>
        /// Updates <see cref="AssignedWire"/> silently — raises PropertyChanged so the
        /// combobox binding updates, but does NOT trigger the save/Revit-write callback.
        /// Used to sync the topology combobox when the diagram canvas changes.
        /// </summary>
        public void SetAssignedWireSilent(string value)
        {
            if (Equals(_assignedWire, value)) return;
            _assignedWire = value;
            OnPropertyChanged(nameof(AssignedWire));
        }

        public ObservableCollection<TopologyNodeViewModel> Children { get; } = new ObservableCollection<TopologyNodeViewModel>();

        // ── Sub-device assignment slot (Device nodes only) ────────────────────

        /// <summary>Unassigned devices available to assign beneath this device.</summary>
        public ObservableCollection<UnassignedDeviceOption> AvailableUnassigned { get; }
            = new ObservableCollection<UnassignedDeviceOption>();

        private bool _isAddSlotOpen;
        /// <summary>Whether the add-slot combobox row is visible beneath this device row.</summary>
        public bool IsAddSlotOpen
        {
            get => _isAddSlotOpen;
            set => SetField(ref _isAddSlotOpen, value);
        }

        /// <summary>Toggles the add-slot row open/closed.</summary>
        public ICommand ToggleAddSlotCommand { get; private set; }

        /// <summary>Starts a Revit element pick session for this device.</summary>
        public ICommand PickFromRevitCommand { get; private set; }

        private readonly Action<TopologyNodeViewModel, UnassignedDeviceOption> _onSubDeviceAssign;
        private int _subDeviceCount;

        /// <summary>Address suffix the next assigned sub-device will receive (e.g. "001.1").</summary>
        public string NextSubAddress
        {
            get
            {
                GraphNode.Properties.TryGetValue("Address", out string addr);
                return (addr ?? "") + "." + (_subDeviceCount + 1);
            }
        }

        private UnassignedDeviceOption _selectedUnassigned;
        /// <summary>The option the user picked in the add-slot combobox; fires the assign callback.</summary>
        public UnassignedDeviceOption SelectedUnassigned
        {
            get => _selectedUnassigned;
            set
            {
                if (SetField(ref _selectedUnassigned, value) && value != null)
                    _onSubDeviceAssign?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Called by MainViewModel after the Revit write succeeds.
        /// Removes the assigned option from the combobox and resets selection.
        /// The refresh that follows will rebuild the node with the sub-device as a child.
        /// </summary>
        public void MarkSubDeviceAdded(UnassignedDeviceOption option)
        {
            _subDeviceCount++;
            OnPropertyChanged(nameof(NextSubAddress));
            AvailableUnassigned.Remove(option);
            // Reset selection without re-firing the callback
            _selectedUnassigned = null;
            OnPropertyChanged(nameof(SelectedUnassigned));
            // Do NOT close IsAddSlotOpen — the node is rebuilt on refresh anyway
        }

        /// <summary>
        /// Called after a successful "pick from Revit" assign.
        /// Increments the sub-device counter so NextSubAddress is correct if the user assigns again.
        /// </summary>
        public void IncrementSubDeviceCount()
        {
            _subDeviceCount++;
            OnPropertyChanged(nameof(NextSubAddress));
            // Do NOT close IsAddSlotOpen — the node is rebuilt on refresh anyway
        }

        public TopologyNodeViewModel(
            Node graphNode,
            Action<TopologyNodeViewModel> onSelect = null,
            Action<TopologyNodeViewModel> onAssignConfig = null,
            IReadOnlyList<string> availableConfigs = null,
            string initialAssignment = null,
            Action<TopologyNodeViewModel> onAssignWire = null,
            IReadOnlyList<string> availableWires = null,
            string initialWire = null,
            string parentLabel = null,
            IReadOnlyList<UnassignedDeviceOption> availableUnassigned = null,
            Action<TopologyNodeViewModel, UnassignedDeviceOption> onSubDeviceAssign = null,
            Action<TopologyNodeViewModel> onPickElementForDevice = null)
        {
            GraphNode = graphNode ?? throw new ArgumentNullException(nameof(graphNode));
            SelectCommand = new RelayCommand(_ => onSelect?.Invoke(this));
            _onAssignConfig         = onAssignConfig;
            _onAssignWire           = onAssignWire;
            _onSubDeviceAssign      = onSubDeviceAssign;
            _onPickElementForDevice = onPickElementForDevice;
            ParentLabel             = parentLabel;
            ToggleAddSlotCommand    = new RelayCommand(_ => IsAddSlotOpen = !IsAddSlotOpen);
            PickFromRevitCommand    = new RelayCommand(_ => { IsAddSlotOpen = false; _onPickElementForDevice?.Invoke(this); });

            // Populate config combobox options: blank entry first (= no assignment)
            if (availableConfigs != null && availableConfigs.Count > 0)
            {
                AvailableConfigs.Add(string.Empty);
                foreach (var c in availableConfigs)
                    AvailableConfigs.Add(c);
            }

            // Populate wire combobox options: blank entry first
            if (availableWires != null && availableWires.Count > 0)
            {
                AvailableWires.Add(string.Empty);
                foreach (var w in availableWires)
                    AvailableWires.Add(w);
            }

            // Seed assignments without firing callbacks
            _assignedConfig = initialAssignment ?? string.Empty;
            _assignedWire   = initialWire ?? string.Empty;

            // Populate unassigned pool (Device nodes only)
            if (availableUnassigned != null)
            {
                foreach (var opt in availableUnassigned)
                    AvailableUnassigned.Add(opt);
            }
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
