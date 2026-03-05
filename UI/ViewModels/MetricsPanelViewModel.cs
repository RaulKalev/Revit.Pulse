using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.Modules.Metrics;
using Pulse.Core.Rules;
using Pulse.Core.Settings;
using Pulse.Core.SystemModel;

namespace Pulse.UI.ViewModels
{
    // ──────────────────────────────────────────────────────────────────────────
    // Row ViewModel for SubCircuit member device listing
    // ──────────────────────────────────────────────────────────────────────────

    public sealed class ScMemberRowViewModel
    {
        public string Name        { get; }
        public string CurrentDraw { get; }
        public string AlarmDraw   { get; }

        internal ScMemberRowViewModel(string name, string currentDraw, string alarmDraw)
        {
            Name        = name;
            CurrentDraw = currentDraw;
            AlarmDraw   = alarmDraw;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Row ViewModel for the Health Status section
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents one row in the Health Status section.
    /// Carries its own Highlight command which fires a callback supplied by
    /// <see cref="MetricsPanelViewModel"/> at construction time.
    /// </summary>
    public sealed class HealthIssueRowViewModel
    {
        public string       Description { get; }
        public int          Count       { get; }
        public HealthStatus Status      { get; }
        public bool         HasCount    => Count > 0;
        public ICommand     HighlightCommand { get; }

        internal HealthIssueRowViewModel(
            HealthIssueItem item,
            Action<IEnumerable<long>> highlightCallback)
        {
            Description = item.Description;
            Count       = item.Count;
            Status      = item.Status;

            var ids = item.AffectedElementIds;
            HighlightCommand = new RelayCommand(
                _ => highlightCallback?.Invoke(ids),
                _ => ids != null && ids.Count > 0);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MetricsPanelViewModel
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ViewModel for the System Metrics panel (System Intelligence Dashboard).
    ///
    /// Exposes all data needed by <see cref="Pulse.UI.Controls.SystemMetricsPanel"/>:
    ///   • Backward-compatible gauge bindings (ShowGauges, MaUsed, MaMax, AddressesUsed, AddressesMax)
    ///   • Header: context-aware summary and overall health status
    ///   • Capacity: structured text summaries alongside gauges
    ///   • Health Status: per-rule issue rows with individual highlight commands
    ///   • Distribution: device-type breakdown
    ///   • Cabling &amp; Spatial: per-loop cable metrics
    ///   • Quick Actions: Run System Check, Highlight Issues, Toggle 3-D Routing, Open BOQ
    ///
    /// All calculations are delegated to <see cref="SystemMetricsCalculator"/>.
    /// No Revit API calls happen here — interaction is handled via action callbacks
    /// injected by <see cref="MainViewModel"/>.
    /// </summary>
    public class MetricsPanelViewModel : ViewModelBase
    {
        // ── State snapshot ────────────────────────────────────────────────────

        private Node      _selectedNode;
        private Panel     _selectedPanel;
        private Loop      _selectedLoop;
        private ModuleData _lastData;
        private TopologyAssignmentsStore _lastAssignments = new TopologyAssignmentsStore();
        private DeviceConfigStore        _lastDeviceStore = new DeviceConfigStore();
        private CapacityMetrics          _lastCap;

        // ── Callbacks injected by MainViewModel ───────────────────────────────

        /// <summary>Called when the user requests element highlighting in Revit.</summary>
        public Action<IEnumerable<long>> HighlightElementsRequested { get; set; }

        /// <summary>Called when the user clicks Toggle 3-D Routing (placeholder).</summary>
        public Action Toggle3DRoutingRequested { get; set; }

        /// <summary>Called when the user clicks Open BOQ (placeholder).</summary>
        public Action OpenBOQRequested { get; set; }

        // ──────────────────────────────────────────────────────────────────────
        // HEADER SECTION
        // ──────────────────────────────────────────────────────────────────────

        private bool _hasSelection;
        public bool HasSelection
        {
            get => _hasSelection;
            private set => SetField(ref _hasSelection, value);
        }

        private string _contextTitle = "No Selection";
        public string ContextTitle
        {
            get => _contextTitle;
            private set => SetField(ref _contextTitle, value);
        }

        private string _contextSubtitle = "Select a panel or loop";
        public string ContextSubtitle
        {
            get => _contextSubtitle;
            private set => SetField(ref _contextSubtitle, value);
        }

        private HealthStatus _overallStatus = HealthStatus.Ok;
        public HealthStatus OverallStatus
        {
            get => _overallStatus;
            private set => SetField(ref _overallStatus, value);
        }

        private string _overallStatusLabel = "OK";
        public string OverallStatusLabel
        {
            get => _overallStatusLabel;
            private set => SetField(ref _overallStatusLabel, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // CAPACITY SECTION  (backward-compat + enriched)
        // ──────────────────────────────────────────────────────────────────────

        // ← existing bindings the XAML gauges rely on
        private bool   _showGauges;
        public  bool   ShowGauges
        {
            get => _showGauges;
            private set => SetField(ref _showGauges, value);
        }

        private int    _addressesUsed;
        public  int    AddressesUsed
        {
            get => _addressesUsed;
            private set => SetField(ref _addressesUsed, value);
        }

        private int    _addressesMax;
        public  int    AddressesMax
        {
            get => _addressesMax;
            private set => SetField(ref _addressesMax, value);
        }

        private double _maUsed;
        public  double MaUsed
        {
            get => _maUsed;
            private set => SetField(ref _maUsed, value);
        }

        private double _maMax;
        public  double MaMax
        {
            get => _maMax;
            private set => SetField(ref _maMax, value);
        }

        // ← new enriched properties
        private string _addressSummary = string.Empty;
        public  string AddressSummary
        {
            get => _addressSummary;
            private set => SetField(ref _addressSummary, value);
        }

        private string _maSummary = string.Empty;
        public  string MaSummary
        {
            get => _maSummary;
            private set => SetField(ref _maSummary, value);
        }

        private string _remainingAddressesSummary = string.Empty;
        public  string RemainingAddressesSummary
        {
            get => _remainingAddressesSummary;
            private set => SetField(ref _remainingAddressesSummary, value);
        }

        private string _remainingMaSummary = string.Empty;
        public  string RemainingMaSummary
        {
            get => _remainingMaSummary;
            private set => SetField(ref _remainingMaSummary, value);
        }

        private CapacityStatus _addressCapacityStatus = CapacityStatus.Normal;
        public  CapacityStatus AddressCapacityStatus
        {
            get => _addressCapacityStatus;
            private set => SetField(ref _addressCapacityStatus, value);
        }

        private CapacityStatus _maCapacityStatus = CapacityStatus.Normal;
        public  CapacityStatus MaCapacityStatus
        {
            get => _maCapacityStatus;
            private set => SetField(ref _maCapacityStatus, value);
        }

        private bool _isCapacitySectionExpanded = true;
        public  bool IsCapacitySectionExpanded
        {
            get => _isCapacitySectionExpanded;
            set => SetField(ref _isCapacitySectionExpanded, value);
        }

        // ── SubCircuit-only capacity properties ───────────────────────────────

        private bool _showSubCircuitMetrics;
        public  bool ShowSubCircuitMetrics
        {
            get => _showSubCircuitMetrics;
            private set
            {
                if (SetField(ref _showSubCircuitMetrics, value))
                    OnPropertyChanged(nameof(CapacitySectionTitle));
            }
        }

        /// <summary>Header label for the capacity section — context-sensitive.</summary>
        public string CapacitySectionTitle
            => ShowSubCircuitMetrics ? "CIRCUIT METRICS" : "CAPACITY";

        // True when neither panel/loop gauges nor SubCircuit metrics are visible
        private bool _isCapacityEmpty = true;
        public  bool IsCapacityEmpty
        {
            get => _isCapacityEmpty;
            private set => SetField(ref _isCapacityEmpty, value);
        }

        private double _maNormal;
        public  double MaNormal
        {
            get => _maNormal;
            private set => SetField(ref _maNormal, value);
        }

        // Maximum mA for the SubCircuit's host loop (0 when no config assigned)
        private double _scMaMax;
        public  double ScMaMax
        {
            get => _scMaMax;
            private set => SetField(ref _scMaMax, value);
        }

        private int _childDeviceCount;
        public  int ChildDeviceCount
        {
            get => _childDeviceCount;
            private set => SetField(ref _childDeviceCount, value);
        }

        // Voltage drop (SubCircuit only)
        private double _scVDropVolts;
        public  double ScVDropVolts
        {
            get => _scVDropVolts;
            private set => SetField(ref _scVDropVolts, value);
        }

        // Maximum tolerable voltage drop (4 V for a 24 V NAC circuit)
        private double _scVDropMax = 4.0;
        public  double ScVDropMax
        {
            get => _scVDropMax;
            private set => SetField(ref _scVDropMax, value);
        }

        private bool _showVDropGauge;
        public  bool ShowVDropGauge
        {
            get => _showVDropGauge;
            private set => SetField(ref _showVDropGauge, value);
        }

        // Nominal supply voltage (from PSU Revit parameter, 0 = unknown)
        private double _scNominalVoltage;
        public  double ScNominalVoltage
        {
            get => _scNominalVoltage;
            private set => SetField(ref _scNominalVoltage, value);
        }

        // Remaining voltage at the far end of the NAC run (NominalVoltage − VDrop)
        private double _scRemainingVolts;
        public  double ScRemainingVolts
        {
            get => _scRemainingVolts;
            private set => SetField(ref _scRemainingVolts, value);
        }

        private bool _showRemainingVoltGauge;
        public  bool ShowRemainingVoltGauge
        {
            get => _showRemainingVoltGauge;
            private set => SetField(ref _showRemainingVoltGauge, value);
        }

        public ObservableCollection<ScMemberRowViewModel> ScMembers { get; }
            = new ObservableCollection<ScMemberRowViewModel>();

        // ──────────────────────────────────────────────────────────────────────
        // HEALTH STATUS SECTION
        // ──────────────────────────────────────────────────────────────────────

        public ObservableCollection<HealthIssueRowViewModel> HealthIssues { get; }
            = new ObservableCollection<HealthIssueRowViewModel>();

        private int _totalHealthIssueCount;
        public  int TotalHealthIssueCount
        {
            get => _totalHealthIssueCount;
            private set => SetField(ref _totalHealthIssueCount, value);
        }

        private bool _isHealthSectionExpanded = true;
        public  bool IsHealthSectionExpanded
        {
            get => _isHealthSectionExpanded;
            set => SetField(ref _isHealthSectionExpanded, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // DISTRIBUTION SECTION
        // ──────────────────────────────────────────────────────────────────────

        public ObservableCollection<DistributionGroup> DistributionGroups { get; }
            = new ObservableCollection<DistributionGroup>();

        private bool _isDistributionSectionExpanded = true;
        public  bool IsDistributionSectionExpanded
        {
            get => _isDistributionSectionExpanded;
            set => SetField(ref _isDistributionSectionExpanded, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // CABLING & SPATIAL SECTION
        // ──────────────────────────────────────────────────────────────────────

        public ObservableCollection<LoopCablingInfo> LoopCablingInfos { get; }
            = new ObservableCollection<LoopCablingInfo>();

        private string _totalCableLengthDisplay = "—";
        public  string TotalCableLengthDisplay
        {
            get => _totalCableLengthDisplay;
            private set => SetField(ref _totalCableLengthDisplay, value);
        }

        private string _longestLoopDisplay = "—";
        public  string LongestLoopDisplay
        {
            get => _longestLoopDisplay;
            private set => SetField(ref _longestLoopDisplay, value);
        }

        private bool _isCablingSectionExpanded = false;
        public  bool IsCablingSectionExpanded
        {
            get => _isCablingSectionExpanded;
            set => SetField(ref _isCablingSectionExpanded, value);
        }

        private bool _showCablingSection;
        public  bool ShowCablingSection
        {
            get => _showCablingSection;
            private set => SetField(ref _showCablingSection, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // QUICK ACTIONS / TOAST
        // ──────────────────────────────────────────────────────────────────────

        private string _toastText = string.Empty;
        public  string ToastText
        {
            get => _toastText;
            private set => SetField(ref _toastText, value);
        }

        private bool _showToast;
        public  bool ShowToast
        {
            get => _showToast;
            private set => SetField(ref _showToast, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // COMMANDS
        // ──────────────────────────────────────────────────────────────────────

        public ICommand RunSystemCheckCommand    { get; }
        public ICommand HighlightIssuesCommand   { get; }
        public ICommand Toggle3DRoutingCommand   { get; }
        public ICommand OpenBOQCommand           { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Construction
        // ──────────────────────────────────────────────────────────────────────

        public MetricsPanelViewModel()
        {
            RunSystemCheckCommand  = new RelayCommand(ExecuteRunSystemCheck);
            HighlightIssuesCommand = new RelayCommand(ExecuteHighlightIssues);
            Toggle3DRoutingCommand = new RelayCommand(_ => Toggle3DRoutingRequested?.Invoke());
            OpenBOQCommand         = new RelayCommand(_ => OpenBOQRequested?.Invoke());
        }

        // ──────────────────────────────────────────────────────────────────────
        // Public update entry-points
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by MainViewModel whenever a topology node is selected.
        /// Pass <c>null</c> for node to clear the panel.
        /// </summary>
        public void LoadNode(
            Node node,
            ModuleData data,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            _selectedNode  = node;
            _lastData      = data;
            _lastAssignments = assignments ?? new TopologyAssignmentsStore();
            _lastDeviceStore = deviceStore  ?? new DeviceConfigStore();

            if (node == null || data == null)
            {
                _selectedPanel = null;
                _selectedLoop  = null;
                ClearMetrics();
                return;
            }

            _selectedPanel = node.NodeType == "Panel"
                ? data.Panels.Find(p => p.EntityId == node.Id)
                : null;

            _selectedLoop  = node.NodeType == "Loop"
                ? data.Loops.Find(l => l.EntityId == node.Id)
                : node.NodeType == "Device"
                ? data.Loops.Find(l => l.Devices.Any(d => d.EntityId == node.Id))
                : null;

            if (_selectedLoop != null && _selectedPanel == null)
            {
                // Resolve parent panel for the selected loop
                _selectedPanel = data.Panels.FirstOrDefault(
                    p => p.Loops.Any(l => l.EntityId == _selectedLoop.EntityId));
            }

            Rebuild();
        }

        /// <summary>
        /// Called by MainViewModel after a full data refresh (topology rebuild).
        /// Re-computes metrics for whichever entity is currently selected.
        /// </summary>
        public void Refresh(
            ModuleData data,
            TopologyAssignmentsStore assignments,
            DeviceConfigStore deviceStore)
        {
            _lastData        = data;
            _lastAssignments = assignments ?? new TopologyAssignmentsStore();
            _lastDeviceStore = deviceStore  ?? new DeviceConfigStore();

            // If we had a node selected, rehydrate it from the fresh data
            if (_selectedNode != null && data != null)
            {
                var freshPanel = _selectedPanel != null
                    ? data.Panels.Find(p => p.EntityId == _selectedPanel.EntityId)
                    : null;
                var freshLoop  = _selectedLoop != null
                    ? data.Loops.Find(l => l.EntityId == _selectedLoop.EntityId)
                    : _selectedNode.NodeType == "Device"
                    ? data.Loops.Find(l => l.Devices.Any(d => d.EntityId == _selectedNode.Id))
                    : null;
                _selectedPanel = freshPanel;
                _selectedLoop  = freshLoop;
            }

            Rebuild();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Private rebuild
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called externally (e.g. from MainViewModel) when only a minor property changes
        /// (such as wire type) so the panel is re-computed without a full data refresh.
        /// </summary>
        public void RebuildMetrics() => Rebuild();

        private void Rebuild()
        {
            if (_lastData == null || _selectedNode == null)
            {
                ClearMetrics();
                return;
            }

            HasSelection = _selectedPanel != null || _selectedLoop != null
                        || _selectedNode?.NodeType == "SubCircuit"
                        || _selectedNode?.NodeType == "SubCircuitMember"
                        || IsSubDeviceNode(_selectedNode);

            // ── Header ──────────────────────────────────────────────────────
            BuildHeader();

            // ── Capacity ────────────────────────────────────────────────────
            BuildCapacity();

            // ── Health ──────────────────────────────────────────────────────
            BuildHealth();

            // ── Distribution ────────────────────────────────────────────────
            BuildDistribution();

            // ── Cabling ─────────────────────────────────────────────────────
            BuildCabling();

            // ── Overall status (after all sections) ─────────────────────────
            var issues = HealthIssues.Select(r => new HealthIssueItem
            {
                Status = r.Status,
                Count  = r.Count
            });
            var capMetrics = ShowGauges ? new CapacityMetrics
            {
                AddressesUsed = AddressesUsed, AddressesMax = AddressesMax,
                MaUsed = MaUsed, MaMax = MaMax
            } : null;

            OverallStatus = SystemMetricsCalculator.ComputeOverallStatus(issues, capMetrics);
            switch (OverallStatus)
            {
                case HealthStatus.Error:   OverallStatusLabel = "Issues";   break;
                case HealthStatus.Warning: OverallStatusLabel = "Warnings"; break;
                default:                   OverallStatusLabel = "OK";        break;
            }
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void BuildHeader()
        {
            if (_selectedNode?.NodeType == "SubCircuit"
             || _selectedNode?.NodeType == "SubCircuitMember"
             || IsSubDeviceNode(_selectedNode))
            {
                ContextTitle    = _selectedNode.Label;
                _selectedNode.Properties.TryGetValue("DeviceCount", out string dcStr);
                int.TryParse(dcStr, out int devCount);
                ContextSubtitle = $"NAC SubCircuit  ·  {devCount} device{(devCount == 1 ? "" : "s")}";
                return;
            }
            if (_selectedLoop != null)
            {
                ContextTitle    = _selectedLoop.DisplayName;
                int devCount    = _selectedLoop.Devices.Count;
                ContextSubtitle = $"Loop  ·  {devCount} device{(devCount == 1 ? "" : "s")}";
            }
            else if (_selectedPanel != null)
            {
                ContextTitle    = _selectedPanel.DisplayName;
                int loopCount   = _selectedPanel.Loops.Count;
                int devCount    = _selectedPanel.Loops.Sum(l => l.Devices.Count);
                ContextSubtitle = $"Panel  ·  {loopCount} loop{(loopCount == 1 ? "" : "s")}  ·  {devCount} devices";
            }
            else
            {
                ContextTitle    = "System Overview";
                ContextSubtitle = $"{_lastData.Panels.Count} panels  ·  {_lastData.Devices.Count} devices";
            }
        }

        // ── Capacity ──────────────────────────────────────────────────────────

        private void BuildCapacity()
        {
            // Reset SubCircuit-specific fields on every rebuild
            ShowSubCircuitMetrics = false;
            MaNormal              = 0;
            ScMaMax               = 0;
            ChildDeviceCount      = 0;
            ShowVDropGauge         = false;
            ScVDropVolts           = 0;
            ScVDropMax             = 4.0;
            ScNominalVoltage       = 0;
            ScRemainingVolts       = 0;
            ShowRemainingVoltGauge = false;

            // ── SubCircuit / SubCircuitMember / sub-device path ──────────────
            if (_selectedNode?.NodeType == "SubCircuit"
             || _selectedNode?.NodeType == "SubCircuitMember"
             || IsSubDeviceNode(_selectedNode))
            {
                ShowGauges    = false;
                AddressesUsed = AddressesMax = 0;
                MaUsed        = MaMax        = 0;
                AddressSummary = MaSummary   = string.Empty;
                RemainingAddressesSummary = RemainingMaSummary = string.Empty;
                AddressCapacityStatus = MaCapacityStatus = CapacityStatus.Normal;

                _selectedNode.Properties.TryGetValue("DeviceCount", out string dcStr);
                int.TryParse(dcStr, out int devCount);
                ChildDeviceCount = devCount;

                // Populate member device list from scmember:: nodes in topology data
                ScMembers.Clear();
                string scRawId = _selectedNode.Id.StartsWith("subcircuit::")
                    ? _selectedNode.Id.Substring("subcircuit::".Length)
                    : _selectedNode.Id;
                foreach (var mn in _lastData.Nodes)
                {
                    if (mn.NodeType != "SubCircuitMember") continue;
                    if (!mn.Properties.TryGetValue("SubCircuitId", out string membSc)
                        || membSc != scRawId) continue;
                    string draw      = mn.Properties.TryGetValue("CurrentDraw",      out string d)  ? d  : "—";
                    string alarmDraw  = mn.Properties.TryGetValue("CurrentDrawAlarm", out string da) ? da : draw;
                    ScMembers.Add(new ScMemberRowViewModel(mn.Label, draw, alarmDraw));
                }

                double maNormal = 0, maAlarm = 0;
                if (_selectedNode.Properties.TryGetValue("TotalMaNormal", out string normalStr))
                    double.TryParse(normalStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out maNormal);
                if (_selectedNode.Properties.TryGetValue("TotalMaAlarm", out string alarmStr))
                    double.TryParse(alarmStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out maAlarm);

                MaNormal = maNormal;
                MaUsed   = maAlarm;

                // Look up host loop's MaMax from the device config store
                if (_lastData != null
                    && _selectedNode.Properties.TryGetValue("HostElementId", out string hostIdStr)
                    && long.TryParse(hostIdStr, out long hostElemId))
                {
                    var hostLoop = _lastData.Loops.FirstOrDefault(
                        l => l.Devices.Any(d => d.RevitElementId == hostElemId));
                    if (hostLoop != null
                        && _lastAssignments.LoopAssignments.TryGetValue(
                               hostLoop.DisplayName, out string modName))
                    {
                        var cfg = _lastDeviceStore.LoopModules
                            .FirstOrDefault(m => m.Name == modName);
                        if (cfg != null) ScMaMax = cfg.MaxMaPerLoop;
                    }
                }

                // ── Voltage-drop calculation ────────────────────────────────────────
                // Cable length is stored on the node as metres (set by topology builder)
                double cableLengthMetres = 0;
                _selectedNode.Properties.TryGetValue("CableLength", out string clStr);
                double.TryParse(clStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out cableLengthMetres);

                // Wire type comes from live assignment store so ComboBox changes reflect immediately
                string vdWireType = null;
                if (_lastAssignments.SubCircuits.TryGetValue(scRawId, out var scAssign)
                    && !string.IsNullOrEmpty(scAssign.WireTypeKey))
                    vdWireType = scAssign.WireTypeKey;

                if (cableLengthMetres > 0 && !string.IsNullOrEmpty(vdWireType))
                {
                    var wire = _lastDeviceStore.Wires.FirstOrDefault(w =>
                        string.Equals(w.Name, vdWireType,
                            System.StringComparison.OrdinalIgnoreCase));
                    if (wire != null && (wire.CoreSizeMm2 > 0 || wire.ResistancePerMetreOhm > 0))
                    {
                        // ── Per-conductor resistance at reference temperature ───────────
                        // Prefer datasheet ResistancePerMetreOhm; fall back to 2ρ/A formula.
                        const double CopperRho20 = 0.0175; // Ω·mm²/m at 20 °C
                        double rPerMetreAt20 = wire.ResistancePerMetreOhm > 0
                            ? wire.ResistancePerMetreOhm
                            : (wire.CoreSizeMm2 > 0 ? CopperRho20 / wire.CoreSizeMm2 : 0);

                        if (rPerMetreAt20 > 0)
                        {
                            // ── Temperature derating (BS 7671 / IEC 60228 coefficient) ──
                            // α = 0.00393 /°C for annealed copper
                            double tempDegC = 20.0;
                            if (_lastAssignments.SubCircuits.TryGetValue(scRawId, out var scTemp))
                                tempDegC = scTemp.CableTemperatureDegC;
                            double tempFactor = 1.0 + 0.00393 * (tempDegC - 20.0);
                            double rPerMetre = rPerMetreAt20 * tempFactor;

                            // ── Distributed-load V-drop ───────────────────────────────
                            // Walk device segments sorted by cumulative distance.
                            // Each segment carries only the current drawn by devices beyond it.
                            // V_drop = Σ [ I_beyond_i × 2 × R/m × segment_length ]
                            // This is accurate when device-distance data is available.
                            // Falls back to worst-case (all current at far end) if not.
                            double vDrop = 0;

                            bool usedDistributed = false;
                            if (_selectedNode.Properties.TryGetValue("DeviceCumulativeDistFeet", out string distCsv)
                                && _selectedNode.Properties.TryGetValue("DeviceAlarmMa", out string maCsv)
                                && !string.IsNullOrEmpty(distCsv) && !string.IsNullOrEmpty(maCsv))
                            {
                                // Parse "elemId:value" CSV pairs — list (sortable) for distances, dict for mA lookup
                                var distEntries = ParseCsvPairsList(distCsv);
                                var maEntries   = ParseCsvPairsDict(maCsv);

                                if (distEntries.Count > 0 && distEntries.Count == maEntries.Count)
                                {
                                    // Sort ascending by distance
                                    distEntries.Sort((a, b) => a.Value.CompareTo(b.Value));

                                    // Total alarm current for all devices (mA → A)
                                    double totalAlarmA = maEntries.Values.Sum() / 1000.0;

                                    double prevDistMetres = 0;
                                    double currentBeyondA = totalAlarmA;

                                    foreach (var entry in distEntries)
                                    {
                                        double segMetres = entry.Value * 0.3048 - prevDistMetres;
                                        if (segMetres < 0) segMetres = 0;

                                        // Segment resistance = 2 conductors
                                        vDrop += currentBeyondA * 2.0 * rPerMetre * segMetres;

                                        // Subtract this device's current for segments beyond it
                                        if (maEntries.TryGetValue(entry.Key, out double devMa))
                                            currentBeyondA -= devMa / 1000.0;
                                        if (currentBeyondA < 0) currentBeyondA = 0;

                                        prevDistMetres = entry.Value * 0.3048;
                                    }
                                    usedDistributed = true;
                                }
                            }

                            if (!usedDistributed)
                            {
                                // Worst-case: all current drawn at far end
                                double rTotal = 2.0 * rPerMetre * cableLengthMetres;
                                vDrop = (maAlarm / 1000.0) * rTotal;
                            }

                            ScVDropVolts = vDrop;

                            // ── Supervisory current (EOL resistor) ────────────────────
                            // Adds V_nom/R_eol to the normal-mode mA gauge
                            double nomVolts = 0;
                            if (_selectedNode.Properties.TryGetValue("NominalVoltage", out string nomVoltStr)
                                && double.TryParse(nomVoltStr,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double parsedNomVolts))
                                nomVolts = parsedNomVolts;

                            if (_lastAssignments.SubCircuits.TryGetValue(scRawId, out var scEol)
                                && scEol.EolResistorOhms > 0 && nomVolts > 0)
                            {
                                double supervisoryMa = (nomVolts / scEol.EolResistorOhms) * 1000.0;
                                MaNormal = MaNormal + supervisoryMa;
                            }

                            // ── Scale gauges by nominal voltage ───────────────────────
                            double vDropLimitPct = 16.7;
                            if (_lastAssignments.SubCircuits.TryGetValue(scRawId, out var scAssignPct))
                                vDropLimitPct = scAssignPct.VDropLimitPct;

                            if (nomVolts > 0)
                            {
                                ScNominalVoltage       = nomVolts;
                                ScVDropMax             = nomVolts * (vDropLimitPct / 100.0);
                                ScRemainingVolts       = Math.Max(0, nomVolts - ScVDropVolts);
                                ShowRemainingVoltGauge = true;
                            }
                            else
                            {
                                ScVDropMax = 24.0 * (vDropLimitPct / 100.0);
                            }
                            ShowVDropGauge = true;
                        }
                    }
                }

                ShowSubCircuitMetrics = true;
                IsCapacityEmpty       = false;
                return;
            }

            // ── Panel / Loop path ─────────────────────────────────────────────
            CapacityMetrics cap = null;
            if (_selectedLoop != null)
                cap = SystemMetricsCalculator.ComputeForLoop(_selectedLoop, _lastAssignments, _lastDeviceStore);
            else if (_selectedPanel != null)
                cap = SystemMetricsCalculator.ComputeForPanel(_selectedPanel, _lastAssignments, _lastDeviceStore);

            if (cap == null)
            {
                ShowGauges    = false;
                AddressesUsed = AddressesMax = 0;
                MaUsed        = MaMax        = 0;
                AddressSummary = MaSummary   = string.Empty;
                RemainingAddressesSummary = RemainingMaSummary = string.Empty;
                AddressCapacityStatus = MaCapacityStatus = CapacityStatus.Normal;
                IsCapacityEmpty = true;
                return;
            }

            ShowGauges    = true;
            AddressesUsed = cap.AddressesUsed;
            AddressesMax  = cap.AddressesMax;
            MaUsed        = cap.MaUsed;
            MaMax         = cap.MaMax;

            AddressSummary             = cap.AddressSummary;
            MaSummary                  = cap.MaSummary;
            RemainingAddressesSummary  = cap.RemainingAddressesSummary;
            RemainingMaSummary         = cap.RemainingMaSummary;
            AddressCapacityStatus      = cap.AddressStatus;
            MaCapacityStatus           = cap.MaStatus;
            _lastCap                   = cap;
            IsCapacityEmpty            = false;
        }

        // ── Health ────────────────────────────────────────────────────────────

        private void BuildHealth()
        {
            // SubCircuit / SubCircuitMember / sub-device — show SubCircuit-specific checks only
            if (_selectedNode?.NodeType == "SubCircuit"
             || _selectedNode?.NodeType == "SubCircuitMember"
             || IsSubDeviceNode(_selectedNode))
            {
                HealthIssues.Clear();
                var scIssues = SystemMetricsCalculator.ComputeSubCircuitVDropIssues(
                    _lastData, _lastAssignments, _lastDeviceStore);
                foreach (var item in scIssues)
                    HealthIssues.Add(new HealthIssueRowViewModel(item, HighlightElementsRequested));
                TotalHealthIssueCount = scIssues.Count;
                return;
            }

            var items = SystemMetricsCalculator.ComputeHealthIssues(
                _lastData,
                panel: _selectedPanel,
                loop:  _selectedLoop);

            HealthIssues.Clear();
            foreach (var item in items)
            {
                HealthIssues.Add(new HealthIssueRowViewModel(
                    item,
                    HighlightElementsRequested));
            }

            // Append capacity warnings derived from the gauges
            foreach (var item in SystemMetricsCalculator.ComputeCapacityHealthIssues(_lastCap))
                HealthIssues.Add(new HealthIssueRowViewModel(item, HighlightElementsRequested));

            TotalHealthIssueCount = HealthIssues.Sum(r => r.Count > 0 ? r.Count : (r.Status != HealthStatus.Ok ? 1 : 0));
        }

        // ── Distribution ──────────────────────────────────────────────────────

        private void BuildDistribution()
        {
            // SubCircuit has no panel/loop context — skip full-system distribution scan
            if (_selectedNode?.NodeType == "SubCircuit")
            {
                DistributionGroups.Clear();
                return;
            }

            IEnumerable<AddressableDevice> devices;
            if (_selectedLoop != null)
                devices = _selectedLoop.Devices;
            else if (_selectedPanel != null)
                devices = _selectedPanel.Loops.SelectMany(l => l.Devices);
            else
                devices = _lastData.Devices;

            var groups = SystemMetricsCalculator.ComputeDistribution(devices);
            DistributionGroups.Clear();
            foreach (var g in groups) DistributionGroups.Add(g);
        }

        // ── Cabling ───────────────────────────────────────────────────────────

        private void BuildCabling()
        {
            if (_selectedPanel == null)
            {
                ShowCablingSection = false;
                LoopCablingInfos.Clear();
                TotalCableLengthDisplay = "—";
                LongestLoopDisplay      = "—";
                return;
            }

            var cabling = SystemMetricsCalculator.ComputeCabling(_selectedPanel, _selectedLoop);
            LoopCablingInfos.Clear();
            foreach (var info in cabling.LoopInfos) LoopCablingInfos.Add(info);
            TotalCableLengthDisplay = cabling.TotalLengthDisplay;
            LongestLoopDisplay      = cabling.LoopInfos.Count > 1
                ? $"{cabling.LongestLoopName}  ({cabling.LongestLoopDisplay})"
                : (cabling.LoopInfos.Count == 1 ? cabling.LoopInfos[0].LengthDisplay : "—");
            ShowCablingSection      = LoopCablingInfos.Count > 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the node is a Device sub-element (child of another Device,
        /// e.g. a base/isolator with a dotted address like "001.1").
        /// The topology builder marks these with Properties["IsSubDevice"] = "true".
        /// </summary>
        private static bool IsSubDeviceNode(Node node)
            => node?.NodeType == "Device"
            && node.Properties.TryGetValue("IsSubDevice", out string v)
            && v == "true";

        // ── Clear ─────────────────────────────────────────────────────────────

        private void ClearMetrics()
        {
            HasSelection    = false;
            ContextTitle    = "No Selection";
            ContextSubtitle = "Select a panel or loop";
            OverallStatus   = HealthStatus.Ok;
            OverallStatusLabel = "OK";
            ShowGauges            = false;
            ShowSubCircuitMetrics  = false;
            MaNormal               = 0;
            ScMaMax                = 0;
            ChildDeviceCount       = 0;
            ScMembers.Clear();
            ShowVDropGauge         = false;
            ScVDropVolts           = 0;
            ScVDropMax             = 4.0;
            IsCapacityEmpty        = true;
            AddressesUsed          = AddressesMax = 0;
            MaUsed                 = MaMax        = 0;
            AddressSummary  = MaSummary    = string.Empty;
            RemainingAddressesSummary = RemainingMaSummary = string.Empty;
            AddressCapacityStatus = MaCapacityStatus = CapacityStatus.Normal;
            HealthIssues.Clear();
            TotalHealthIssueCount = 0;
            DistributionGroups.Clear();
            LoopCablingInfos.Clear();
            TotalCableLengthDisplay = "—";
            LongestLoopDisplay      = "—";
            ShowCablingSection      = false;
            _lastCap                = null;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Command handlers
        // ──────────────────────────────────────────────────────────────────────

        private void ExecuteRunSystemCheck(object _)
        {
            string prompt = SystemCheckPromptBuilder.Build(
                _lastData, _lastAssignments, _lastDeviceStore);

            if (string.IsNullOrEmpty(prompt))
            {
                ShowTransientToast("No data to export. Refresh first.");
                return;
            }

            try { Clipboard.SetText(prompt); }
            catch { /* Clipboard may be locked by another process */ }

            var state = UiStateService.Load();
            if (state.SuppressAiPromptPopup)
            {
                ShowTransientToast("AI review prompt copied to clipboard.");
            }
            else
            {
                ShowAiPromptPopup(suppress =>
                {
                    if (suppress)
                    {
                        state.SuppressAiPromptPopup = true;
                        UiStateService.Save(state);
                    }
                });
            }
        }

        private void ExecuteHighlightIssues(object _)
        {
            if (_lastData == null) return;

            // Re-derive element IDs directly from the rule results so we don't need
            // to expose them on HealthIssueRowViewModel.
            var items  = SystemMetricsCalculator.ComputeHealthIssues(
                _lastData, _selectedPanel, _selectedLoop);
            var allIds = items
                .SelectMany(i => i.AffectedElementIds)
                .Distinct()
                .ToList();

            HighlightElementsRequested?.Invoke(allIds);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Toast helper
        // ──────────────────────────────────────────────────────────────────────

        private DispatcherTimer _toastTimer;

        private void ShowTransientToast(string message)
        {
            ToastText = message;
            ShowToast = true;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, e) =>
            {
                ShowToast = false;
                _toastTimer.Stop();
            };
            _toastTimer.Start();
        }

        // ──────────────────────────────────────────────────────────────────────
        // AI Prompt Popup (code-only dialog — keeps XAML count low)
        // ──────────────────────────────────────────────────────────────────────

        // ── CSV helpers for distributed V-drop ─────────────────────────────────

        /// <summary>
        /// Parses "elemId:value,elemId:value,..." into a sortable list of kvp pairs.
        /// </summary>
        private static List<System.Collections.Generic.KeyValuePair<int, double>> ParseCsvPairsList(string csv)
        {
            var result = new List<System.Collections.Generic.KeyValuePair<int, double>>();
            if (string.IsNullOrEmpty(csv)) return result;
            foreach (var part in csv.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                if (int.TryParse(part.Substring(0, colon), out int key)
                    && double.TryParse(part.Substring(colon + 1),
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out double val))
                    result.Add(new System.Collections.Generic.KeyValuePair<int, double>(key, val));
            }
            return result;
        }

        /// <summary>
        /// Parses "elemId:value,elemId:value,..." into a dictionary keyed by element id.
        /// Duplicate ids keep the last value.
        /// </summary>
        private static Dictionary<int, double> ParseCsvPairsDict(string csv)
        {
            var result = new Dictionary<int, double>();
            if (string.IsNullOrEmpty(csv)) return result;
            foreach (var part in csv.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                if (int.TryParse(part.Substring(0, colon), out int key)
                    && double.TryParse(part.Substring(colon + 1),
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out double val))
                    result[key] = val;
            }
            return result;
        }

        private static void ShowAiPromptPopup(Action<bool> onClosed)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var win = new AiPromptInfoWindow();
                win.ShowDialog();
                onClosed?.Invoke(win.DontShowAgain);
            });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Plugin-themed code-only dialog for AI prompt guidance
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// WPF popup shown after "Run System Check" copies the AI prompt to the
    /// clipboard.  Styled to match the Pulse dark theme — borderless, rounded,
    /// transparent chrome, custom title bar.
    /// </summary>
    internal sealed class AiPromptInfoWindow : Window
    {
        // ── theme palette ────────────────────────────────────────────────────
        private static readonly System.Windows.Media.Color ColPrimaryBg   = System.Windows.Media.Color.FromRgb(0x1F, 0x1F, 0x1F);
        private static readonly System.Windows.Media.Color ColSecondaryBg = System.Windows.Media.Color.FromRgb(0x29, 0x29, 0x29);
        private static readonly System.Windows.Media.Color ColBorder      = System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44);
        private static readonly System.Windows.Media.Color ColForeground  = System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE);
        private static readonly System.Windows.Media.Color ColSubtle      = System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80);
        private static readonly System.Windows.Media.Color ColAccent      = System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD7);
        private static readonly System.Windows.Media.Color ColHighlight   = System.Windows.Media.Color.FromRgb(0x50, 0x50, 0x50);
        private static readonly System.Windows.Media.Color ColBulletBg    = System.Windows.Media.Color.FromRgb(0x23, 0x23, 0x23);

        private static System.Windows.Media.SolidColorBrush Brush(System.Windows.Media.Color c) =>
            new System.Windows.Media.SolidColorBrush(c);

        // ── public surface ───────────────────────────────────────────────────
        public bool DontShowAgain => _suppressToggle?.IsChecked == true;
        private System.Windows.Controls.Primitives.ToggleButton _suppressToggle;

        public AiPromptInfoWindow()
        {
            // ── chrome ───────────────────────────────────────────────────────
            Title                 = "AI Review Prompt Copied";
            Width                 = 400;
            SizeToContent         = SizeToContent.Height;
            ResizeMode            = ResizeMode.NoResize;
            WindowStyle           = WindowStyle.None;
            AllowsTransparency    = true;
            Background            = System.Windows.Media.Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ── drop shadow ──────────────────────────────────────────────────
            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color       = System.Windows.Media.Colors.Black,
                BlurRadius  = 20,
                ShadowDepth = 0,
                Opacity     = 0.65,
            };

            // ── outer card border ────────────────────────────────────────────
            var card = new System.Windows.Controls.Border
            {
                Background      = Brush(ColSecondaryBg),
                BorderBrush     = Brush(ColBorder),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(9),
                Margin          = new Thickness(12),
                Effect          = shadow,
            };

            var root = new System.Windows.Controls.StackPanel();
            card.Child = root;

            // ── title bar — matches TitleBar.xaml (Height=30, CornerRadius=9,9,0,0) ──
            var titleBar = new System.Windows.Controls.Border
            {
                Background   = Brush(ColPrimaryBg),
                CornerRadius = new CornerRadius(9, 9, 0, 0),
                Height       = 30,
            };
            // Drag-to-move on the title bar only
            titleBar.MouseLeftButtonDown += (s, e) => DragMove();

            var titleDock = new System.Windows.Controls.DockPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Background        = System.Windows.Media.Brushes.Transparent,
                Margin            = new Thickness(10, 0, 0, 0),
            };

            // Close button — Width=35, Height=19, CloseCircleOutline icon, red on hover
            var closeIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind              = MaterialDesignThemes.Wpf.PackIconKind.CloseCircleOutline,
                Width             = 20,
                Height            = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Foreground        = Brush(ColSubtle),
            };
            var closeBtn = new System.Windows.Controls.Button
            {
                Width           = 35,
                Height          = 19,
                Background      = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Content         = closeIcon,
            };
            // Inline template so the button background stays transparent
            var closeBdFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            closeBdFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            var closeCpFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            closeCpFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeCpFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeBdFactory.AppendChild(closeCpFactory);
            closeBtn.Template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button)) { VisualTree = closeBdFactory };
            closeBtn.MouseEnter  += (s, e) => closeIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x6B, 0x6B));
            closeBtn.MouseLeave  += (s, e) => closeIcon.Foreground = Brush(ColSubtle);
            closeBtn.PreviewMouseLeftButtonDown += (s, e) => closeIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB4, 0x2C, 0x2C));
            closeBtn.Click       += (s, e) => Close();
            System.Windows.Controls.DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);

            var titleText = new System.Windows.Controls.TextBlock
            {
                Text              = "AI Review Prompt",
                FontSize          = 14,
                FontWeight        = FontWeights.Bold,
                Foreground        = Brush(ColForeground),
                VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Controls.DockPanel.SetDock(titleText, System.Windows.Controls.Dock.Left);

            titleDock.Children.Add(closeBtn);
            titleDock.Children.Add(titleText);
            titleBar.Child = titleDock;
            root.Children.Add(titleBar);

            // ── body ─────────────────────────────────────────────────────────
            var body = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(18, 16, 18, 18),
            };

            // Icon + headline row
            var headRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 10),
            };
            var clipIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind              = MaterialDesignThemes.Wpf.PackIconKind.ContentPaste,
                Width             = 22,
                Height            = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 10, 0),
                Foreground        = Brush(ColAccent),
            };
            var headline = new System.Windows.Controls.TextBlock
            {
                Text              = "Prompt copied to clipboard!",
                FontSize          = 13,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = Brush(ColForeground),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping      = TextWrapping.Wrap,
            };
            headRow.Children.Add(clipIcon);
            headRow.Children.Add(headline);
            body.Children.Add(headRow);

            // Sub-description
            body.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text         = "Paste it into your AI assistant and ask for a review:",
                FontSize     = 11,
                Foreground   = Brush(ColSubtle),
                Margin       = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            });

            // Bullet card
            var bulletCard = new System.Windows.Controls.Border
            {
                Background      = Brush(ColBulletBg),
                BorderBrush     = Brush(ColBorder),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(5),
                Padding         = new Thickness(12, 8, 12, 8),
                Margin          = new Thickness(0, 0, 0, 14),
            };
            var bullets = new System.Windows.Controls.StackPanel();
            string[] bulletItems = new[]
            {
                "Compliance review  ·  NFPA 72 / EN 54",
                "Capacity optimisation suggestions",
                "Loop balance recommendations",
                "Device placement feedback",
            };
            foreach (var item in bulletItems)
            {
                var row = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin      = new Thickness(0, 2, 0, 2),
                };
                row.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text              = "›",
                    FontSize          = 11,
                    Foreground        = Brush(ColAccent),
                    Margin            = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                row.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text              = item,
                    FontSize          = 11,
                    Foreground        = Brush(ColForeground),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                bullets.Children.Add(row);
            }
            bulletCard.Child = bullets;
            body.Children.Add(bulletCard);

            // Separator
            body.Children.Add(new System.Windows.Controls.Border
            {
                Height     = 1,
                Background = Brush(ColBorder),
                Margin     = new Thickness(0, 0, 0, 12),
            });

            // ── footer: suppress toggle + OK button ──────────────────────────
            var footRow = new System.Windows.Controls.Grid();
            footRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            // Suppress toggle — icon swaps CheckboxBlankOutline ↔ CheckboxMarked
            var suppressIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind              = MaterialDesignThemes.Wpf.PackIconKind.CheckboxBlankOutline,
                Width             = 15,
                Height            = 15,
                Margin            = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = Brush(ColSubtle),
            };
            var suppressLabel = new System.Windows.Controls.TextBlock
            {
                Text              = "Don't show this again",
                FontSize          = 10,
                Foreground        = Brush(ColSubtle),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var suppressContent = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
            };
            suppressContent.Children.Add(suppressIcon);
            suppressContent.Children.Add(suppressLabel);

            _suppressToggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content         = suppressContent,
                Background      = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Padding         = new Thickness(0),
            };
            // Flat transparent template
            var togBdFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            togBdFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            var togCpFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            togCpFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            togCpFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            togBdFactory.AppendChild(togCpFactory);
            _suppressToggle.Template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton)) { VisualTree = togBdFactory };
            _suppressToggle.Checked   += (s, e) =>
            {
                suppressIcon.Kind      = MaterialDesignThemes.Wpf.PackIconKind.CheckboxMarked;
                suppressIcon.Foreground = Brush(ColAccent);
                suppressLabel.Foreground = Brush(ColForeground);
            };
            _suppressToggle.Unchecked += (s, e) =>
            {
                suppressIcon.Kind      = MaterialDesignThemes.Wpf.PackIconKind.CheckboxBlankOutline;
                suppressIcon.Foreground = Brush(ColSubtle);
                suppressLabel.Foreground = Brush(ColSubtle);
            };
            System.Windows.Controls.Grid.SetColumn(_suppressToggle, 0);

            // "Got it" button — matches PulsePrimaryButtonStyle (blue, CornerRadius=5, highlight on hover)
            var okBdFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            okBdFactory.Name = "OkBd";
            okBdFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, Brush(ColAccent));
            okBdFactory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(5));
            okBdFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(12, 6, 12, 6));
            var okCpFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            okCpFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            okCpFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            okBdFactory.AppendChild(okCpFactory);
            var okTemplate = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            okTemplate.VisualTree = okBdFactory;
            var okHoverTrigger = new System.Windows.Trigger
            {
                Property = System.Windows.UIElement.IsMouseOverProperty,
                Value    = true,
            };
            var okHoverSetter = new System.Windows.Setter(System.Windows.Controls.Border.BackgroundProperty, Brush(ColHighlight));
            okHoverSetter.TargetName = "OkBd";
            okHoverTrigger.Setters.Add(okHoverSetter);
            okTemplate.Triggers.Add(okHoverTrigger);

            var okBtn = new System.Windows.Controls.Button
            {
                Content   = "Got it",
                FontSize  = 11,
                Foreground = System.Windows.Media.Brushes.White,
                Cursor     = System.Windows.Input.Cursors.Hand,
                Template   = okTemplate,
            };
            okBtn.Click += (s, e) => Close();
            System.Windows.Controls.Grid.SetColumn(okBtn, 1);

            footRow.Children.Add(_suppressToggle);
            footRow.Children.Add(okBtn);
            body.Children.Add(footRow);

            root.Children.Add(body);
            Content = card;
        }
    }
}
