using System;
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
            set
            {
                SetField(ref _showGauges, value);
                OnPropertyChanged(nameof(IsCapacityEmpty));
            }
        }

        // ── Label editing (SubCircuit only) ──────────────────────────────────

        private bool _showLabelEdit;
        /// <summary>True only for SubCircuit nodes — enables double-click rename in the header.</summary>
        public bool ShowLabelEdit
        {
            get => _showLabelEdit;
            set => SetField(ref _showLabelEdit, value);
        }

        private bool _isEditingLabel;
        public bool IsEditingLabel
        {
            get => _isEditingLabel;
            set => SetField(ref _isEditingLabel, value);
        }

        private string _editingLabelText;
        public string EditingLabelText
        {
            get => _editingLabelText;
            set => SetField(ref _editingLabelText, value);
        }

        /// <summary>
        /// Raised when the user commits a renamed SubCircuit label.
        /// Parameters: (scNodeId, newName) where scNodeId is the full node id, e.g. "subcircuit::{guid}".
        /// </summary>
        public event Action<string, string> SubCircuitLabelCommitted;

        // ── V-Drop limit (SubCircuit only) ────────────────────────────────

        private bool _showVDropEdit;
        public bool ShowVDropEdit
        {
            get => _showVDropEdit;
            set => SetField(ref _showVDropEdit, value);
        }

        private string _vDropDisplay;
        public string VDropDisplay
        {
            get => _vDropDisplay;
            set => SetField(ref _vDropDisplay, value);
        }

        private bool _isEditingVDrop;
        public bool IsEditingVDrop
        {
            get => _isEditingVDrop;
            set => SetField(ref _isEditingVDrop, value);
        }

        private string _editingVDropText;
        public string EditingVDropText
        {
            get => _editingVDropText;
            set => SetField(ref _editingVDropText, value);
        }

        /// <summary>Raised when the user commits an edited V-Drop limit. Parameters: (scNodeId, newPct).</summary>
        public event Action<string, double> VDropLimitPctCommitted;

        // ── Current draw (Device only) ────────────────────────────────────

        private bool _showCurrentDraw;
        public bool ShowCurrentDraw
        {
            get => _showCurrentDraw;
            set => SetField(ref _showCurrentDraw, value);
        }

        private string _currentDrawNormal;
        public string CurrentDrawNormal
        {
            get => _currentDrawNormal;
            set => SetField(ref _currentDrawNormal, value);
        }

        private string _currentDrawAlarm;
        public string CurrentDrawAlarm
        {
            get => _currentDrawAlarm;
            set => SetField(ref _currentDrawAlarm, value);
        }

        // ── Current draw inline editing ───────────────────────────────────

        private bool _isEditingCurrentDrawNormal;
        public bool IsEditingCurrentDrawNormal
        {
            get => _isEditingCurrentDrawNormal;
            set => SetField(ref _isEditingCurrentDrawNormal, value);
        }

        private bool _isEditingCurrentDrawAlarm;
        public bool IsEditingCurrentDrawAlarm
        {
            get => _isEditingCurrentDrawAlarm;
            set => SetField(ref _isEditingCurrentDrawAlarm, value);
        }

        private string _editingCurrentDrawNormalText;
        public string EditingCurrentDrawNormalText
        {
            get => _editingCurrentDrawNormalText;
            set => SetField(ref _editingCurrentDrawNormalText, value);
        }

        private string _editingCurrentDrawAlarmText;
        public string EditingCurrentDrawAlarmText
        {
            get => _editingCurrentDrawAlarmText;
            set => SetField(ref _editingCurrentDrawAlarmText, value);
        }

        /// <summary>
        /// Raised when the user commits an edited current-draw value.
        /// Parameters: (revitElementId, isAlarm, newValue).
        /// MainViewModel subscribes to write the value to the Revit element.
        /// </summary>
        public event Action<long, bool, string> CurrentDrawValueCommitted;

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

        // ── SubCircuit-specific metrics ───────────────────────────────────

        private bool _showSubCircuitMetrics;
        /// <summary>True when a SubCircuit node is selected — shows the NAC mA summary block.</summary>
        public bool ShowSubCircuitMetrics
        {
            get => _showSubCircuitMetrics;
            set
            {
                SetField(ref _showSubCircuitMetrics, value);
                OnPropertyChanged(nameof(IsCapacityEmpty));
            }
        }

        private double _maNormal;
        /// <summary>Aggregate normal-mode current draw for the selected SubCircuit (mA).</summary>
        public double MaNormal
        {
            get => _maNormal;
            set => SetField(ref _maNormal, value);
        }

        /// <summary>
        /// True when neither the panel/loop gauges nor the SubCircuit metrics block has data.
        /// Drives the "Select a panel or loop" empty-state label.
        /// </summary>
        public bool IsCapacityEmpty => !ShowGauges && !ShowSubCircuitMetrics;

        /// <summary>Key-value properties of the selected entity.</summary>
        public ObservableCollection<PropertyItem> Properties { get; } = new ObservableCollection<PropertyItem>();

        /// <summary>Warnings associated with the selected entity.</summary>
        public ObservableCollection<WarningItem> Warnings { get; } = new ObservableCollection<WarningItem>();

        /// <summary>
        /// Load the inspector with data from the selected node.
        /// </summary>
        public void LoadNode(Node node, ModuleData data)
        {
            // Cancel any in-progress edit when selection changes.
            CancelEdit();

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

            // Reset metric sections — each type branch opts back in selectively
            ShowGauges            = false;
            ShowSubCircuitMetrics = false;
            MaNormal              = 0;
            MaUsed                = 0;

            // Load properties
            Properties.Clear();
            ShowCurrentDraw = false;
            CurrentDrawNormal = string.Empty;
            CurrentDrawAlarm = string.Empty;

            foreach (var kvp in node.Properties)
            {
                // Current draw values are surfaced in their own dedicated section.
                if (kvp.Key == "Current draw normal")
                {
                    ShowCurrentDraw = true;
                    CurrentDrawNormal = kvp.Value;
                    continue;
                }
                if (kvp.Key == "Current draw alarm")
                {
                    ShowCurrentDraw = true;
                    CurrentDrawAlarm = kvp.Value;
                    continue;
                }
                // Format cable length with unit suffix for display.
                if (kvp.Key == "CableLength")
                {
                    Properties.Add(new PropertyItem { Key = "Cable length", Value = kvp.Value + " m" });
                    continue;
                }
                // ── SubCircuit property labels ────────────────────────────────────
                if (node.NodeType == "SubCircuit")
                {
                    if (kvp.Key == "DeviceCount")
                    { Properties.Add(new PropertyItem { Key = "Devices", Value = kvp.Value }); continue; }
                    if (kvp.Key == "HostElementId")
                    { Properties.Add(new PropertyItem { Key = "Host element ID", Value = kvp.Value }); continue; }
                    if (kvp.Key == "TotalMaNormal")
                    { Properties.Add(new PropertyItem { Key = "Load (normal)", Value = kvp.Value + " mA" }); continue; }
                    if (kvp.Key == "TotalMaAlarm")
                    { Properties.Add(new PropertyItem { Key = "Load (alarm)", Value = kvp.Value + " mA" }); continue; }
                    if (kvp.Key == "WireType")
                    { Properties.Add(new PropertyItem { Key = "Wire type", Value = kvp.Value }); continue; }
                }                // ── SubCircuitMember property labels ──────────────────────────────────
                else if (node.NodeType == "SubCircuitMember")
                {
                    if (kvp.Key == "SubCircuitId")
                    { Properties.Add(new PropertyItem { Key = "Circuit ID", Value = kvp.Value }); continue; }
                    if (kvp.Key == "MemberElementId")
                    { Properties.Add(new PropertyItem { Key = "Element ID", Value = kvp.Value }); continue; }
                    if (kvp.Key == "CurrentDraw")
                    { Properties.Add(new PropertyItem { Key = "Current draw", Value = kvp.Value + " mA" }); continue; }
                }                Properties.Add(new PropertyItem { Key = kvp.Key, Value = kvp.Value });
            }

            // For Device nodes, resolve "Panel type" from the live assignments store
            // (FA_Panel_Config on the Revit element is only populated after a Revit write-back).
            if (node.NodeType == "Device")
            {
                ShowCurrentDraw = true; // always show the section for devices
                var panelTypeProp = Properties.FirstOrDefault(p => p.Key == "Panel type");
                string panelName = node.Properties.TryGetValue("Panel", out string pn) ? pn : null;
                if (!string.IsNullOrEmpty(panelName)
                    && AssignmentsStore.PanelAssignments.TryGetValue(panelName, out string assignedConfig)
                    && !string.IsNullOrEmpty(assignedConfig))
                {
                    if (panelTypeProp != null)
                        panelTypeProp.Value = assignedConfig;
                    else
                    {
                        // Insert after "Panel" row
                        int panelIdx = Properties.IndexOf(Properties.FirstOrDefault(p => p.Key == "Panel"));
                        var item = new PropertyItem { Key = "Panel type", Value = assignedConfig };
                        if (panelIdx >= 0)
                            Properties.Insert(panelIdx + 1, item);
                        else
                            Properties.Add(item);
                    }
                }
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

                // Reset label-edit mode (only SubCircuit enables it below)
                ShowLabelEdit = false;

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
                else if (node.NodeType == "SubCircuit")
                {
                    node.Properties.TryGetValue("DeviceCount", out string dcStr);
                    int.TryParse(dcStr, out int dc);
                    ChildDeviceCount = dc;

                    // mA loads from the aggregated graph-node properties
                    double maNormal = 0, maAlarm = 0;
                    if (node.Properties.TryGetValue("TotalMaNormal", out string mnStr))
                        double.TryParse(mnStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out maNormal);
                    if (node.Properties.TryGetValue("TotalMaAlarm", out string maStr))
                        double.TryParse(maStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out maAlarm);

                    MaNormal = maNormal;
                    MaUsed   = maAlarm;   // reuse MaUsed for alarm draw (most critical)

                    ShowGauges = false;
                    ShowSubCircuitMetrics = true;
                    ShowLabelEdit = true;

                    // V-Drop limit — load from assignments store
                    ShowVDropEdit = true;
                    double vDropPct = 16.7;
                    string scRawId = node.Id.StartsWith("subcircuit::", StringComparison.Ordinal)
                        ? node.Id.Substring("subcircuit::".Length)
                        : node.Id;
                    if (AssignmentsStore.SubCircuits.TryGetValue(scRawId, out var scAssign))
                        vDropPct = scAssign.VDropLimitPct;
                    VDropDisplay = vDropPct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " %";
                }
                else if (node.NodeType == "SubCircuitMember")
                {
                    ChildDeviceCount = 0;
                    ShowGauges = false;
                    ShowSubCircuitMetrics = false;
                }
                else
                {
                    ChildDeviceCount = 0;
                    ShowGauges = false;
                    ShowSubCircuitMetrics = false;
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
            ShowSubCircuitMetrics = false;
            ShowCurrentDraw = false;
            CurrentDrawNormal = string.Empty;
            CurrentDrawAlarm = string.Empty;
            IsEditingCurrentDrawNormal = false;
            IsEditingCurrentDrawAlarm = false;
            EditingCurrentDrawNormalText = string.Empty;
            EditingCurrentDrawAlarmText = string.Empty;
            ShowLabelEdit = false;
            IsEditingLabel = false;
            EditingLabelText = string.Empty;
            ShowVDropEdit = false;
            VDropDisplay = string.Empty;
            IsEditingVDrop = false;
            EditingVDropText = string.Empty;
            AddressesUsed = 0;
            AddressesMax  = 0;
            MaUsed        = 0;
            MaMax         = 0;
            MaNormal      = 0;
            Properties.Clear();
            Warnings.Clear();
        }

        // ── Current draw edit actions ─────────────────────────────────────

        public void BeginEditNormal()
        {
            if (!RevitElementId.HasValue) return;
            IsEditingCurrentDrawAlarm = false;
            EditingCurrentDrawNormalText = CurrentDrawNormal ?? string.Empty;
            IsEditingCurrentDrawNormal = true;
        }

        public void BeginEditAlarm()
        {
            if (!RevitElementId.HasValue) return;
            IsEditingCurrentDrawNormal = false;
            EditingCurrentDrawAlarmText = CurrentDrawAlarm ?? string.Empty;
            IsEditingCurrentDrawAlarm = true;
        }

        public void CommitNormal()
        {
            if (!RevitElementId.HasValue || !IsEditingCurrentDrawNormal) return;
            IsEditingCurrentDrawNormal = false;
            var newVal = EditingCurrentDrawNormalText ?? string.Empty;
            CurrentDrawNormal = newVal; // optimistic update
            CurrentDrawValueCommitted?.Invoke(RevitElementId.Value, false, newVal);
        }

        public void CommitAlarm()
        {
            if (!RevitElementId.HasValue || !IsEditingCurrentDrawAlarm) return;
            IsEditingCurrentDrawAlarm = false;
            var newVal = EditingCurrentDrawAlarmText ?? string.Empty;
            CurrentDrawAlarm = newVal; // optimistic update
            CurrentDrawValueCommitted?.Invoke(RevitElementId.Value, true, newVal);
        }

        public void CancelEdit()
        {
            IsEditingCurrentDrawNormal = false;
            IsEditingCurrentDrawAlarm = false;
            IsEditingLabel = false;
            IsEditingVDrop = false;
        }

        public void BeginEditLabel()
        {
            EditingLabelText = Title ?? string.Empty;
            IsEditingLabel = true;
        }

        public void CommitLabel()
        {
            if (!IsEditingLabel) return;
            IsEditingLabel = false;
            var newName = EditingLabelText ?? string.Empty;
            Title = newName; // optimistic update
            SubCircuitLabelCommitted?.Invoke(EntityId, newName);
        }

        public void BeginEditVDrop()
        {
            IsEditingCurrentDrawNormal = false;
            IsEditingCurrentDrawAlarm = false;
            IsEditingLabel = false;
            // strip trailing " %" to get bare number for editing
            EditingVDropText = VDropDisplay?.Replace("%", "").Trim() ?? "16.7";
            IsEditingVDrop = true;
        }

        public void CommitVDrop()
        {
            if (!IsEditingVDrop) return;
            IsEditingVDrop = false;
            if (!double.TryParse(EditingVDropText ?? "16.7",
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pct))
                return;
            pct = pct < 1.0 ? 1.0 : pct > 50.0 ? 50.0 : pct;
            VDropDisplay = pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " %";
            VDropLimitPctCommitted?.Invoke(EntityId, pct);
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
            AddressesMax  = cfg.MaxAddresses > 0 ? cfg.MaxAddresses : cfg.AddressesPerLoop * loopCount;
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
