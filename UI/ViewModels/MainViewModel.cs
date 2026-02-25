using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.Settings;
using Pulse.Core.Graph;
using Pulse.Modules.FireAlarm;
using Pulse.Revit.Services;
using Pulse.Revit.Storage;
using Pulse.UI;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Root ViewModel for the Pulse main window.
    /// Owns UI bindings and commands; delegates orchestration to extracted services:
    ///   - <see cref="PulseAppController"/>      — module registry, active module, settings state
    ///   - <see cref="RefreshPipeline"/>          — collect/build/evaluate via ExternalEvent
    ///   - <see cref="SelectionHighlightFacade"/> — select/highlight/reset in Revit
    ///   - <see cref="StorageFacade"/>            — safe ES and JSON persistence
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;

        // ── Orchestration services ───────────────────────────────────────────
        private readonly PulseAppController _appController;
        private readonly RefreshPipeline _refreshPipeline;
        private readonly SelectionHighlightFacade _selectionFacade;
        private readonly StorageFacade _storageFacade;

        /// <summary>Per-document topology assignments — loaded from ES at startup and kept in sync.</summary>
        private TopologyAssignmentsStore _topologyAssignments = new TopologyAssignmentsStore();

        /// <summary>Custom symbol library — loaded from %APPDATA%\Pulse\custom-symbols.json at startup.</summary>
        private List<CustomSymbolDefinition> _symbolLibrary = CustomSymbolLibraryService.Load();

        // Child ViewModels
        public TopologyViewModel Topology { get; }
        public InspectorViewModel Inspector { get; }
        public DiagramViewModel Diagram { get; }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand ResetOverridesCommand { get; }
        public ICommand FilterWarningsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenDeviceConfiguratorCommand { get; }
        public ICommand OpenSymbolMappingCommand { get; }

        // Status
        private string _statusText = "Ready. Press Refresh to load system data.";
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private int _totalDevices;
        public int TotalDevices
        {
            get => _totalDevices;
            set => SetField(ref _totalDevices, value);
        }

        private int _totalWarnings;
        public int TotalWarnings
        {
            get => _totalWarnings;
            set => SetField(ref _totalWarnings, value);
        }

        private int _totalErrors;
        public int TotalErrors
        {
            get => _totalErrors;
            set => SetField(ref _totalErrors, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                SetField(ref _isLoading, value);
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _showWarningsOnly;
        public bool ShowWarningsOnly
        {
            get => _showWarningsOnly;
            set
            {
                if (SetField(ref _showWarningsOnly, value))
                {
                    ApplyFilter();
                }
            }
        }

        private string _activeModuleName = "Fire Alarm";
        public string ActiveModuleName
        {
            get => _activeModuleName;
            set => SetField(ref _activeModuleName, value);
        }

        // Owner window — set via Initialize() so settings dialog can use it as parent
        private Window _ownerWindow;

        public MainViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));

            // ── Initialise orchestration services ────────────────────────────
            _appController = new PulseAppController();

            // Use reflection-based module discovery with manual fallback.
            // The fallback list ensures that if reflection fails to find
            // FireAlarmModuleDefinition, the module is still registered.
            var fallback = new IModuleDefinition[] { new FireAlarmModuleDefinition() };
            _appController.DiscoverModules(fallback);

            ActiveModuleName = _appController.ActiveModule.DisplayName;

            _refreshPipeline = new RefreshPipeline();
            _selectionFacade = new SelectionHighlightFacade();
            _storageFacade = new StorageFacade();

            // Create child ViewModels
            Topology = new TopologyViewModel();
            Inspector = new InspectorViewModel();
            Diagram = new DiagramViewModel();

            // Wire up topology selection events
            Topology.NodeSelected     += OnTopologyNodeSelected;
            Topology.ConfigAssigned   += OnTopologyConfigAssigned;
            Topology.WireAssigned     += OnTopologyWireAssigned;

            // Wire diagram visibility saves
            Diagram.VisibilityChanged = () =>
                _storageFacade.SaveDiagramSettings(Diagram.Visibility);

            // Wire topology assignment saves (panel/loop configs, flip states, etc.)
            Action saveAssignments = () =>
                _storageFacade.SaveTopologyAssignments(_topologyAssignments);
            Topology.AssignmentsSaveRequested = saveAssignments;
            Diagram.AssignmentsSaveRequested = saveAssignments;

            // Wire diagram wire-assignment writes
            Diagram.WireAssigned = OnDiagramWireAssigned;

            // Create commands
            RefreshCommand = new RelayCommand(ExecuteRefresh, () => !IsLoading);
            ResetOverridesCommand = new RelayCommand(ExecuteResetOverrides);
            FilterWarningsCommand = new RelayCommand(_ => ShowWarningsOnly = !ShowWarningsOnly);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            OpenDeviceConfiguratorCommand = new RelayCommand(ExecuteOpenDeviceConfigurator);
            OpenSymbolMappingCommand = new RelayCommand(ExecuteOpenSymbolMapping);

            // Load previously saved settings from Extensible Storage (we are on the API thread here)
            LoadInitialSettings(uiApp.ActiveUIDocument?.Document);
        }

        /// <summary>
        /// Call this from the View's code-behind immediately after setting DataContext.
        /// Stores a reference to the parent window used to position the settings dialog.
        /// </summary>
        public void Initialize(Window owner)
        {
            _ownerWindow = owner;
        }

        /// <summary>
        /// Execute a data refresh from Revit.
        /// Delegates to <see cref="RefreshPipeline"/> which runs via ExternalEvent.
        /// </summary>
        private void ExecuteRefresh()
        {
            if (IsLoading || _appController.ActiveModule == null) return;

            IsLoading = true;
            StatusText = "Collecting data from Revit...";

            _refreshPipeline.Execute(
                _appController.ActiveModule,
                _appController.ActiveSettings,
                data =>
                {
                    // Callback may come from Revit thread; marshal to UI thread
                    Application.Current?.Dispatcher?.Invoke(() => OnDataCollected(data));
                },
                ex =>
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StatusText = $"Error: {ex.Message}";
                        IsLoading = false;
                    });
                });
        }

        /// <summary>
        /// Called when the module collector completes successfully.
        /// Updates the topology view, inspector, and status.
        /// </summary>
        private void OnDataCollected(ModuleData data)
        {
            _appController.OnRefreshCompleted(data);

            // Reload topology assignments from Extensible Storage (the handler reads them
            // on the Revit API thread during Execute so they are fresh here).
            if (_refreshPipeline.RefreshedAssignments != null)
                _topologyAssignments = _refreshPipeline.RefreshedAssignments;

            // Update statistics
            TotalDevices = data.Devices.Count;
            TotalWarnings = data.WarningCount;
            TotalErrors = data.ErrorCount;

            // Update topology (assignments must be set before LoadFromModuleData builds the tree)
            Topology.LoadAssignments(_topologyAssignments);
            Topology.LoadFromModuleData(data);
            Topology.RestoreExpandState(UiStateService.Load().ExpandedNodeIds);

            // Update diagram levels and panels
            var devStore = DeviceConfigService.Load();
            Inspector.DeviceStore = devStore;
            Inspector.AssignmentsStore = _topologyAssignments;
            Diagram.LoadLevels(data.Levels);
            Diagram.LoadAssignments(_topologyAssignments);
            Diagram.LoadLevelElevationOffsets(_topologyAssignments);
            Diagram.LoadPanels(data.Panels, data.Loops, devStore);

            // Update status
            int panelCount = data.Panels.Count;
            int loopCount = data.Loops.Count;
            StatusText = $"{TotalDevices} devices | {panelCount} panels | {loopCount} loops | {TotalWarnings} warnings | {TotalErrors} errors";

            // Apply current filter
            ApplyFilter();

            IsLoading = false;
        }

        /// <summary>
        /// Handle topology node selection — update inspector and select in Revit.
        /// </summary>
        private void OnTopologyNodeSelected(Node node)
        {
            if (node == null)
            {
                Inspector.Clear();
                return;
            }

            Inspector.LoadNode(node, _appController.CurrentData);

            // If the node has a Revit element, select it in the model
            if (node.RevitElementId.HasValue)
            {
                _selectionFacade.SelectElement(
                    node.RevitElementId.Value,
                    GetNeighbourIds(node));
            }
        }

        /// <summary>
        /// For a Device node, returns the Revit element IDs of the previous and
        /// next device on the same loop (sorted by numeric address).
        /// Returns an empty list for non-device nodes or when no neighbours exist.
        /// </summary>
        private List<long> GetNeighbourIds(Node node)
        {
            var result = new List<long>();
            var currentData = _appController.CurrentData;

            if (currentData == null || node.NodeType != "Device") return result;

            // Find the device entry matching this node
            var device = currentData.Devices.Find(
                d => d.RevitElementId.HasValue && d.RevitElementId.Value == node.RevitElementId.Value);
            if (device == null) return result;

            // Get all sibling devices on the same loop, sorted by numeric address
            var siblings = currentData.Devices
                .FindAll(d => d.LoopId == device.LoopId && d.RevitElementId.HasValue);

            siblings.Sort((a, b) => ParseAddress(a.Address).CompareTo(ParseAddress(b.Address)));

            int idx = siblings.FindIndex(d => d.RevitElementId.Value == node.RevitElementId.Value);
            if (idx < 0) return result;

            if (idx > 0)
                result.Add(siblings[idx - 1].RevitElementId.Value);
            if (idx < siblings.Count - 1)
                result.Add(siblings[idx + 1].RevitElementId.Value);

            return result;
        }

        private static int ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return int.MaxValue;
            for (int i = 0; i < address.Length; i++)
            {
                if (!char.IsDigit(address[i])) continue;
                int end = i;
                while (end < address.Length && char.IsDigit(address[end])) end++;
                if (int.TryParse(address.Substring(i, end - i), out int v)) return v;
            }
            return int.MaxValue;
        }

        /// <summary>
        /// Select a specific element in Revit by ElementId value.
        /// Called from the inspector or topology click.
        /// </summary>
        public void SelectInRevit(long elementId)
        {
            _selectionFacade.SelectElement(elementId);
        }

        /// <summary>
        /// Highlight elements with warnings/errors in the Revit view.
        /// </summary>
        public void HighlightWarnings()
        {
            _selectionFacade.HighlightWarnings(_appController.CurrentData);
        }

        /// <summary>
        /// Reset all temporary graphic overrides.
        /// </summary>
        private void ExecuteResetOverrides()
        {
            _selectionFacade.ResetOverrides();
        }

        /// <summary>
        /// Apply or remove the warnings-only filter on the topology.
        /// </summary>
        private void ApplyFilter()
        {
            var currentData = _appController.CurrentData;
            if (currentData == null) return;

            if (ShowWarningsOnly)
            {
                // Get entity IDs of items with warnings
                var warningEntityIds = new HashSet<string>(
                    currentData.RuleResults
                        .Where(r => r.Severity >= Severity.Warning && r.EntityId != null)
                        .Select(r => r.EntityId));

                Topology.FilterToEntities(warningEntityIds);
            }
            else
            {
                Topology.ClearFilter();
            }
        }

        // ─── Settings ────────────────────────────────────────────────────────────

        /// <summary>
        /// Read previously saved settings from Extensible Storage and apply them.
        /// Called once during construction while still on the Revit API thread.
        /// </summary>
        private void LoadInitialSettings(Document doc)
        {
            if (doc == null) return;
            try
            {
                // Parameter mappings are always loaded from local JSON (machine-wide).
                var jsonStore = DeviceConfigService.Load();
                if (jsonStore.ModuleSettings.TryGetValue(_appController.ActiveModule.ModuleId, out var jsonSettings))
                {
                    // Merge in any parameter keys present in defaults but absent in the saved
                    // settings (e.g. keys added in a newer version of the plugin).
                    var defaults = _appController.ActiveModule.GetDefaultSettings();
                    var existingKeys = new HashSet<string>(
                        jsonSettings.ParameterMappings.ConvertAll(m => m.LogicalName),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var dm in defaults.ParameterMappings)
                    {
                        if (!existingKeys.Contains(dm.LogicalName))
                            jsonSettings.ParameterMappings.Add(dm);
                    }

                    _appController.ApplySettings(jsonSettings);
                }

                var diagramSettings = _storageFacade.ReadDiagramSettings(doc);
                if (diagramSettings != null)
                    Diagram.LoadVisibility(diagramSettings);

                // Load per-document topology assignments
                _topologyAssignments = _storageFacade.ReadTopologyAssignments(doc);
                Inspector.DeviceStore = DeviceConfigService.Load();
                Inspector.AssignmentsStore = _topologyAssignments;
            }
            catch
            {
                // Silently fall back to defaults — storage may not exist yet
            }
        }

        /// <summary>Open the Device Configurator dialog.</summary>
        private void ExecuteOpenDeviceConfigurator()
        {
            var vm = new DeviceConfigViewModel();
            var win = new DeviceConfiguratorWindow(vm)
            {
                Owner = _ownerWindow
            };
            win.ShowDialog();
            Diagram.DeviceConfigChanged?.Invoke();
        }

        /// <summary>Open the Symbol Mapping dialog.</summary>
        private void ExecuteOpenSymbolMapping()
        {
            var vm = new SymbolMappingViewModel(_appController.CurrentData, _topologyAssignments.SymbolMappings, _symbolLibrary);

            vm.Saved += mappings =>
            {
                _topologyAssignments.SymbolMappings =
                    new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase);
                _storageFacade.SaveTopologyAssignments(_topologyAssignments);
            };

            var win = new SymbolMappingWindow(vm)
            {
                Owner = _ownerWindow
            };

            // When the user designs a new symbol, persist it to the library file
            win.SymbolCreated += definition =>
            {
                _symbolLibrary.RemoveAll(s => s.Id == definition.Id || s.Name == definition.Name);
                _symbolLibrary.Add(definition);
                CustomSymbolLibraryService.Save(_symbolLibrary);
            };

            win.ShowDialog();
        }

        /// <summary>Save topology expand state to %APPDATA%\Pulse\ui-state.json.</summary>
        public void SaveExpandState()
        {
            var state = UiStateService.Load();
            state.ExpandedNodeIds = new HashSet<string>(Topology.GetExpandedNodeIds());
            UiStateService.Save(state);
        }

        /// <summary>
        /// Called when the user assigns a config in a panel/loop combobox.
        /// Writes the config name to the appropriate Revit parameter on all descendant devices.
        /// </summary>
        private void OnTopologyConfigAssigned(TopologyNodeViewModel vm)
        {
            if (vm == null) return;

            var settings = _appController.ActiveSettings;
            string paramName = null;
            if (vm.NodeType == "Panel")
                paramName = settings?.GetRevitParameterName(FireAlarmParameterKeys.PanelConfig);
            else if (vm.NodeType == "Loop")
                paramName = settings?.GetRevitParameterName(FireAlarmParameterKeys.LoopModuleConfig);

            if (string.IsNullOrEmpty(paramName)) return;

            var configName = vm.AssignedConfig ?? string.Empty;

            List<(long, string, string)> writes;
            string writeTarget;

            if (vm.NodeType == "Panel")
            {
                // Panel config -> write to the panel board element itself.
                // Fall back to descendant devices if no panel element was resolved.
                if (vm.GraphNode.RevitElementId.HasValue)
                {
                    writes = new List<(long, string, string)>
                    {
                        (vm.GraphNode.RevitElementId.Value, paramName, configName)
                    };
                    writeTarget = "FACP element";
                }
                else if (vm.DescendantDeviceElementIds.Count > 0)
                {
                    writes = vm.DescendantDeviceElementIds
                        .Select(id => (id, paramName, configName))
                        .ToList();
                    writeTarget = "descendant devices (no FACP element resolved)";
                }
                else
                {
                    StatusText = $"Config '{configName}' not written — no Revit element resolved for '{vm.Label}'.";
                    return;
                }
            }
            else // Loop
            {
                if (vm.DescendantDeviceElementIds.Count == 0)
                {
                    StatusText = $"Config '{configName}' not written — no device elements resolved for '{vm.Label}'.";
                    return;
                }
                writes = vm.DescendantDeviceElementIds
                    .Select(id => (id, paramName, configName))
                    .ToList();
                writeTarget = $"devices in {vm.Label}";
            }

            _storageFacade.WriteParameters(
                writes,
                count => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Config '{configName}' written to {writeTarget} ({paramName})."
                        : $"Write succeeded but 0 elements updated — check that '{paramName}' exists as a writable string parameter on the {writeTarget}."),
                ex => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write config: {ex.Message}"));
        }

        /// <summary>
        /// Called when the user assigns a wire type to a loop in the topology tree.
        /// Writes the wire name to the configured Revit parameter on all descendant devices.
        /// </summary>
        private void OnTopologyWireAssigned(TopologyNodeViewModel vm)
        {
            if (vm == null) return;

            string paramName = _appController.ActiveSettings?.GetRevitParameterName(FireAlarmParameterKeys.Wire);
            if (string.IsNullOrEmpty(paramName)) return;

            var wireName = vm.AssignedWire ?? string.Empty;

            if (vm.DescendantDeviceElementIds.Count == 0)
            {
                StatusText = $"Wire '{wireName}' not written — no device elements resolved for '{vm.Label}'.";
                return;
            }

            _storageFacade.WriteParameters(
                vm.DescendantDeviceElementIds.Select(id => (id, paramName, wireName)).ToList(),
                count => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Wire '{wireName}' written to {count} device(s) ({paramName})."
                        : $"Write succeeded but 0 elements updated — check that '{paramName}' exists as a writable string parameter on devices."),
                ex => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write wire: {ex.Message}"));

            // Sync diagram canvas — update in-memory assignment and redraw
            Diagram.SyncLoopWire(vm.ParentLabel ?? string.Empty, vm.Label, vm.AssignedWire);
        }

        /// <summary>
        /// Called when the user assigns a wire type to a loop in the diagram.
        /// Writes the wire name to the configured Revit parameter on all descendant devices.
        /// </summary>
        private void OnDiagramWireAssigned(string panelName, string loopName, string wireName)
        {
            // Always sync topology combobox immediately — independent of Revit write path
            var topoNode = Topology.FindLoopNode(panelName, loopName);
            topoNode?.SetAssignedWireSilent(wireName ?? string.Empty);

            var currentData = _appController.CurrentData;
            if (currentData == null) return;

            string paramName = _appController.ActiveSettings?.GetRevitParameterName(
                FireAlarmParameterKeys.Wire);
            if (string.IsNullOrEmpty(paramName)) return;

            // Find the loop by display name
            var loop = currentData.Loops.Find(l =>
                string.Equals(l.DisplayName, loopName, StringComparison.OrdinalIgnoreCase));
            if (loop == null) return;

            var elementIds = loop.Devices
                .Where(d => d.RevitElementId.HasValue)
                .Select(d => d.RevitElementId.Value)
                .ToList();

            if (elementIds.Count == 0)
            {
                StatusText = $"Wire '{wireName}' saved, but no Revit elements found for loop '{loopName}'.";
                return;
            }

            _storageFacade.WriteParameters(
                elementIds.Select(id => (id, paramName, wireName ?? string.Empty)).ToList(),
                count => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Wire '{wireName}' written to {count} devices in loop '{loopName}' ({paramName})."
                        : $"Wire '{wireName}' saved but 0 elements updated — check that '{paramName}' exists as a writable string parameter."),
                ex => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write wire: {ex.Message}"));
        }

        /// <summary>Open the Settings dialog for the active module.</summary>
        private void ExecuteOpenSettings()
        {
            var activeModule = _appController.ActiveModule;
            var defaults = activeModule.GetDefaultSettings();
            var settingsVm = new SettingsViewModel(
                _appController.ActiveSettings, defaults,
                activeModule.DisplayName,
                activeModule.Description);

            settingsVm.SettingsSaved += OnSettingsSaved;

            var settingsWin = new SettingsWindow(settingsVm)
            {
                Owner = _ownerWindow
            };
            settingsWin.ShowDialog();
        }

        /// <summary>
        /// Called when the user confirms new settings in the dialog.
        /// Updates the active settings in memory and persists them asynchronously.
        /// </summary>
        private void OnSettingsSaved(ModuleSettings newSettings)
        {
            _appController.ApplySettings(newSettings);
            StatusText = "Settings applied. Press Refresh to reload data.";

            _storageFacade.SaveSettings(
                newSettings,
                () => Application.Current?.Dispatcher?.Invoke(() => StatusText = "Settings saved to document."),
                ex => Application.Current?.Dispatcher?.Invoke(() => StatusText = $"Could not save settings: {ex.Message}"));
        }
    }
}