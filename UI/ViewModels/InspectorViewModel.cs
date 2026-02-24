using System.Collections.ObjectModel;
using System.Linq;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the inspector panel.
    /// Shows details of the currently selected topology node.
    /// </summary>
    public class InspectorViewModel : ViewModelBase
    {
        private string _title;
        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        private string _entityType;
        public string EntityType
        {
            get => _entityType;
            set => SetField(ref _entityType, value);
        }

        private string _entityId;
        public string EntityId
        {
            get => _entityId;
            set => SetField(ref _entityId, value);
        }

        private long? _revitElementId;
        public long? RevitElementId
        {
            get => _revitElementId;
            set => SetField(ref _revitElementId, value);
        }

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set => SetField(ref _warningCount, value);
        }

        private int _childDeviceCount;
        public int ChildDeviceCount
        {
            get => _childDeviceCount;
            set => SetField(ref _childDeviceCount, value);
        }

        private bool _hasSelection;
        public bool HasSelection
        {
            get => _hasSelection;
            set => SetField(ref _hasSelection, value);
        }

        // ── Gauge data (Panel / Loop only) ────────────────────────────────

        /// <summary>
        /// Device library store (panel/loop/wire type definitions). Set by MainViewModel.
        /// Used to look up assigned config specs for gauge calculations.
        /// </summary>
        public DeviceConfigStore DeviceStore { get; set; } = new DeviceConfigStore();

        /// <summary>
        /// Per-document topology assignments store. Set by MainViewModel.
        /// Used to determine which config is assigned to the selected node.
        /// </summary>
        public TopologyAssignmentsStore AssignmentsStore { get; set; } = new TopologyAssignmentsStore();

        private bool _showGauges;
        public bool ShowGauges
        {
            get => _showGauges;
            set => SetField(ref _showGauges, value);
        }

        private int _addressesUsed;
        public int AddressesUsed
        {
            get => _addressesUsed;
            set => SetField(ref _addressesUsed, value);
        }

        private int _addressesMax;
        public int AddressesMax
        {
            get => _addressesMax;
            set => SetField(ref _addressesMax, value);
        }

        private double _maUsed;
        public double MaUsed
        {
            get => _maUsed;
            set => SetField(ref _maUsed, value);
        }

        private double _maMax;
        public double MaMax
        {
            get => _maMax;
            set => SetField(ref _maMax, value);
        }

        /// <summary>Key-value properties of the selected entity.</summary>
        public ObservableCollection<PropertyItem> Properties { get; } = new ObservableCollection<PropertyItem>();

        /// <summary>Warnings associated with the selected entity.</summary>
        public ObservableCollection<WarningItem> Warnings { get; } = new ObservableCollection<WarningItem>();

        /// <summary>
        /// Load the inspector with data from the selected node.
        /// </summary>
        public void LoadNode(Node node, ModuleData data)
        {
            if (node == null)
            {
                Clear();
                return;
            }

            HasSelection = true;
            Title = node.Label;
            EntityType = node.NodeType;
            EntityId = node.Id;
            RevitElementId = node.RevitElementId;

            // Load properties
            Properties.Clear();
            foreach (var kvp in node.Properties)
            {
                Properties.Add(new PropertyItem { Key = kvp.Key, Value = kvp.Value });
            }

            // Load warnings for this entity
            Warnings.Clear();
            if (data != null)
            {
                var entityWarnings = data.RuleResults
                    .Where(r => r.EntityId == node.Id)
                    .ToList();

                WarningCount = entityWarnings.Count(r => r.Severity >= Severity.Warning);

                foreach (var result in entityWarnings)
                {
                    Warnings.Add(new WarningItem
                    {
                        Severity = result.Severity,
                        RuleName = result.RuleName,
                        Message = result.Message
                    });
                }

                // Count child devices for panels and loops
                if (node.NodeType == "Panel")
                {
                    var panel = data.Panels.Find(p => p.EntityId == node.Id);
                    ChildDeviceCount = panel?.Loops.Sum(l => l.Devices.Count) ?? 0;
                    LoadPanelGauges(node.Label, panel);
                }
                else if (node.NodeType == "Loop")
                {
                    var loop = data.Loops.Find(l => l.EntityId == node.Id);
                    ChildDeviceCount = loop?.Devices.Count ?? 0;
                    LoadLoopGauges(node.Label, loop);
                }
                else
                {
                    ChildDeviceCount = 0;
                    ShowGauges = false;
                }
            }
        }

        /// <summary>Clear the inspector panel.</summary>
        public void Clear()
        {
            HasSelection  = false;
            Title         = null;
            EntityType    = null;
            EntityId      = null;
            RevitElementId = null;
            WarningCount  = 0;
            ChildDeviceCount = 0;
            ShowGauges    = false;
            AddressesUsed = 0;
            AddressesMax  = 0;
            MaUsed        = 0;
            MaMax         = 0;
            Properties.Clear();
            Warnings.Clear();
        }

        // ── Gauge helpers ─────────────────────────────────────────────────

        private void LoadPanelGauges(string panelLabel, Pulse.Core.SystemModel.Panel panel)
        {
            ShowGauges = false;
            if (panel == null) return;

            if (!AssignmentsStore.PanelAssignments.TryGetValue(panelLabel, out string assignedName)) return;

            var cfg = DeviceStore.ControlPanels.FirstOrDefault(p => p.Name == assignedName);
            if (cfg == null) return;

            int loopCount = System.Math.Max(panel.Loops.Count, 1);
            AddressesMax  = cfg.AddressesPerLoop * loopCount;
            MaMax         = cfg.MaxMaPerLoop * loopCount;
            AddressesUsed = panel.Loops.Sum(l => l.Devices.Count);
            MaUsed        = panel.Loops.Sum(l => l.Devices.Sum(d => d.CurrentDraw ?? 0));
            ShowGauges    = true;
        }

        private void LoadLoopGauges(string loopLabel, Pulse.Core.SystemModel.Loop loop)
        {
            ShowGauges = false;
            if (loop == null) return;

            if (!AssignmentsStore.LoopAssignments.TryGetValue(loopLabel, out string assignedName)) return;

            var cfg = DeviceStore.LoopModules.FirstOrDefault(m => m.Name == assignedName);
            if (cfg == null) return;

            AddressesMax  = cfg.AddressesPerLoop;
            MaMax         = cfg.MaxMaPerLoop;
            AddressesUsed = loop.Devices.Count;
            MaUsed        = loop.Devices.Sum(d => d.CurrentDraw ?? 0);
            ShowGauges    = true;
        }
    }

    /// <summary>Represents a single key-value property in the inspector.</summary>
    public class PropertyItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Represents a single warning in the inspector.</summary>
    public class WarningItem
    {
        public Severity Severity { get; set; }
        public string RuleName { get; set; }
        public string Message { get; set; }

        public string SeverityLabel => Severity.ToString().ToUpperInvariant();
    }
}
