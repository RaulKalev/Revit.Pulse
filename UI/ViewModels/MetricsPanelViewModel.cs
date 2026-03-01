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
                    : null;
                _selectedPanel = freshPanel;
                _selectedLoop  = freshLoop;
            }

            Rebuild();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Private rebuild
        // ──────────────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            if (_lastData == null || _selectedNode == null)
            {
                ClearMetrics();
                return;
            }

            HasSelection = _selectedPanel != null || _selectedLoop != null;

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
            CapacityMetrics cap = null;
            if (_selectedLoop != null)
                cap = SystemMetricsCalculator.ComputeForLoop(_selectedLoop, _lastAssignments, _lastDeviceStore);
            else if (_selectedPanel != null)
                cap = SystemMetricsCalculator.ComputeForPanel(_selectedPanel, _lastAssignments, _lastDeviceStore);

            if (cap == null)
            {
                ShowGauges = false;
                AddressesUsed = AddressesMax = 0;
                MaUsed = MaMax = 0;
                AddressSummary = MaSummary = string.Empty;
                RemainingAddressesSummary = RemainingMaSummary = string.Empty;
                AddressCapacityStatus = MaCapacityStatus = CapacityStatus.Normal;
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
        }

        // ── Health ────────────────────────────────────────────────────────────

        private void BuildHealth()
        {
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

        // ── Clear ─────────────────────────────────────────────────────────────

        private void ClearMetrics()
        {
            HasSelection    = false;
            ContextTitle    = "No Selection";
            ContextSubtitle = "Select a panel or loop";
            OverallStatus   = HealthStatus.Ok;
            OverallStatusLabel = "OK";
            ShowGauges      = false;
            AddressesUsed   = AddressesMax = 0;
            MaUsed          = MaMax        = 0;
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
