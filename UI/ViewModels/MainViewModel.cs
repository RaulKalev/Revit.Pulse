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
using Pulse.Revit.ExternalEvents;
using Pulse.Revit.Storage;
using Pulse.UI;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Root ViewModel for the Pulse main window.
    /// Coordinates module selection, data collection, topology display, and inspector.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;

        // Module infrastructure
        private readonly List<IModuleDefinition> _modules = new List<IModuleDefinition>();
        private IModuleDefinition _activeModule;
        private ModuleSettings _activeSettings;
        private ModuleData _currentData;

        // ExternalEvent handlers
        private readonly CollectDevicesHandler _collectHandler;
        private readonly ExternalEvent _collectEvent;
        private readonly SelectElementHandler _selectHandler;
        private readonly ExternalEvent _selectEvent;
        private readonly TemporaryOverrideHandler _overrideHandler;
        private readonly ExternalEvent _overrideEvent;
        private readonly ResetOverridesHandler _resetHandler;
        private readonly ExternalEvent _resetEvent;
        private readonly SaveSettingsHandler _saveSettingsHandler;
        private readonly ExternalEvent _saveSettingsEvent;
        private readonly WriteParameterHandler _writeParamHandler;
        private readonly ExternalEvent _writeParamEvent;
        private readonly SaveDiagramSettingsHandler _saveDiagramHandler;
        private readonly ExternalEvent _saveDiagramEvent;
        private readonly SaveTopologyAssignmentsHandler _saveAssignmentsHandler;
        private readonly ExternalEvent _saveAssignmentsEvent;

        /// <summary>Per-document topology assignments — loaded from ES at startup and kept in sync.</summary>
        private TopologyAssignmentsStore _topologyAssignments = new TopologyAssignmentsStore();

        /// <summary>Custom symbol library — loaded from %APPDATA%\Pulse\custom-symbols.json at startup.</summary>
        private System.Collections.Generic.List<Pulse.Core.Settings.CustomSymbolDefinition> _symbolLibrary
            = Pulse.Core.Settings.CustomSymbolLibraryService.Load();

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

            // Register modules
            _modules.Add(new FireAlarmModuleDefinition());
            _activeModule = _modules[0];
            _activeSettings = _activeModule.GetDefaultSettings();
            ActiveModuleName = _activeModule.DisplayName;

            // Create child ViewModels
            Topology = new TopologyViewModel();
            Inspector = new InspectorViewModel();
            Diagram = new DiagramViewModel();

            // Wire up topology selection events
            Topology.NodeSelected     += OnTopologyNodeSelected;
            Topology.ConfigAssigned   += OnTopologyConfigAssigned;
            Topology.WireAssigned     += OnTopologyWireAssigned;

            // Create ExternalEvent handlers
            _collectHandler = new CollectDevicesHandler();
            _collectEvent = ExternalEvent.Create(_collectHandler);

            _selectHandler = new SelectElementHandler();
            _selectEvent = ExternalEvent.Create(_selectHandler);

            _overrideHandler = new TemporaryOverrideHandler();
            _overrideEvent = ExternalEvent.Create(_overrideHandler);

            _resetHandler = new ResetOverridesHandler();
            _resetEvent = ExternalEvent.Create(_resetHandler);

            _saveSettingsHandler = new SaveSettingsHandler();
            _saveSettingsEvent = ExternalEvent.Create(_saveSettingsHandler);

            _writeParamHandler = new WriteParameterHandler();
            _writeParamEvent   = ExternalEvent.Create(_writeParamHandler);

            _saveDiagramHandler = new SaveDiagramSettingsHandler();
            _saveDiagramEvent   = ExternalEvent.Create(_saveDiagramHandler);

            _saveAssignmentsHandler = new SaveTopologyAssignmentsHandler();
            _saveAssignmentsEvent   = ExternalEvent.Create(_saveAssignmentsHandler);

            // Wire diagram visibility saves
            Diagram.VisibilityChanged = () =>
            {
                _saveDiagramHandler.Settings = Diagram.Visibility;
                _saveDiagramEvent.Raise();
            };

            // Wire topology assignment saves (panel/loop configs, flip states, etc.)
            Action saveAssignments = () =>
            {
                _saveAssignmentsHandler.Store = _topologyAssignments;
                _saveAssignmentsEvent.Raise();
            };
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
        /// Raises an ExternalEvent that runs the module collector on the Revit API thread.
        /// </summary>
        private void ExecuteRefresh()
        {
            if (IsLoading || _activeModule == null) return;

            IsLoading = true;
            StatusText = "Collecting data from Revit...";

            _collectHandler.Collector = _activeModule.CreateCollector();
            _collectHandler.TopologyBuilder = _activeModule.CreateTopologyBuilder();
            _collectHandler.RulePack = _activeModule.CreateRulePack();
            _collectHandler.Settings = _activeSettings;

            _collectHandler.OnCompleted = data =>
            {
                // This callback may come from the Revit thread; marshal to UI thread
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OnDataCollected(data);
                });
            };

            _collectHandler.OnError = ex =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusText = $"Error: {ex.Message}";
                    IsLoading = false;
                });
            };

            _collectEvent.Raise();
        }

        /// <summary>
        /// Called when the module collector completes successfully.
        /// Updates the topology view, inspector, and status.
        /// </summary>
        private void OnDataCollected(ModuleData data)
        {
            _currentData = data;

            // Reload topology assignments from Extensible Storage (the handler reads them
            // on the Revit API thread during Execute so they are fresh here).
            if (_collectHandler.RefreshedAssignments != null)
                _topologyAssignments = _collectHandler.RefreshedAssignments;

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

            Inspector.LoadNode(node, _currentData);

            // If the node has a Revit element, select it in the model
            if (node.RevitElementId.HasValue)
            {
                _selectHandler.ElementIdToSelect = node.RevitElementId.Value;
                _selectHandler.ContextIds = GetNeighbourIds(node);
                _selectEvent.Raise();
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

            if (_currentData == null || node.NodeType != "Device") return result;

            // Find the device entry matching this node
            var device = _currentData.Devices.Find(
                d => d.RevitElementId.HasValue && d.RevitElementId.Value == node.RevitElementId.Value);
            if (device == null) return result;

            // Get all sibling devices on the same loop, sorted by numeric address
            var siblings = _currentData.Devices
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
            _selectHandler.ElementIdToSelect = elementId;
            _selectEvent.Raise();
        }

        /// <summary>
        /// Highlight elements with warnings/errors in the Revit view.
        /// </summary>
        public void HighlightWarnings()
        {
            if (_currentData == null) return;

            var ids = _currentData.RuleResults
                .Where(r => r.ElementId.HasValue && r.Severity >= Severity.Warning)
                .Select(r => r.ElementId.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0) return;

            _overrideHandler.ElementIds = ids;
            _overrideHandler.ColorR = 255;
            _overrideHandler.ColorG = 100;
            _overrideHandler.ColorB = 100;
            _overrideEvent.Raise();
        }

        /// <summary>
        /// Reset all temporary graphic overrides.
        /// </summary>
        private void ExecuteResetOverrides()
        {
            _resetHandler.OverrideService = _overrideHandler.OverrideService;
            _resetEvent.Raise();
        }

        /// <summary>
        /// Apply or remove the warnings-only filter on the topology.
        /// </summary>
        private void ApplyFilter()
        {
            if (_currentData == null) return;

            if (ShowWarningsOnly)
            {
                // Get entity IDs of items with warnings
                var warningEntityIds = new HashSet<string>(
                    _currentData.RuleResults
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
                var service = new ExtensibleStorageService(doc);

                // Parameter mappings are always loaded from local JSON (machine-wide).
                var jsonStore = DeviceConfigService.Load();
                if (jsonStore.ModuleSettings.TryGetValue(_activeModule.ModuleId, out var jsonSettings))
                {
                    // Merge in any parameter keys present in defaults but absent in the saved
                    // settings (e.g. keys added in a newer version of the plugin).
                    var defaults = _activeModule.GetDefaultSettings();
                    var existingKeys = new System.Collections.Generic.HashSet<string>(
                        jsonSettings.ParameterMappings.ConvertAll(m => m.LogicalName),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var dm in defaults.ParameterMappings)
                    {
                        if (!existingKeys.Contains(dm.LogicalName))
                            jsonSettings.ParameterMappings.Add(dm);
                    }

                    _activeSettings = jsonSettings;
                }

                var diagramSettings = service.ReadDiagramSettings();
                if (diagramSettings != null)
                    Diagram.LoadVisibility(diagramSettings);

                // Load per-document topology assignments
                _topologyAssignments = service.ReadTopologyAssignments();
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
            var vm = new SymbolMappingViewModel(_currentData, _topologyAssignments.SymbolMappings, _symbolLibrary);

            vm.Saved += mappings =>
            {
                _topologyAssignments.SymbolMappings =
                    new System.Collections.Generic.Dictionary<string, string>(
                        mappings, System.StringComparer.OrdinalIgnoreCase);
                _saveAssignmentsHandler.Store = _topologyAssignments;
                _saveAssignmentsEvent.Raise();
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
                Pulse.Core.Settings.CustomSymbolLibraryService.Save(_symbolLibrary);
            };

            win.ShowDialog();
        }

        /// <summary>Save topology expand state to %APPDATA%\Pulse\ui-state.json.</summary>
        public void SaveExpandState()
        {
            var state = UiStateService.Load();
            state.ExpandedNodeIds = new System.Collections.Generic.HashSet<string>(Topology.GetExpandedNodeIds());
            UiStateService.Save(state);
        }

        /// <summary>
        /// Called when the user assigns a config in a panel/loop combobox.
        /// Writes the config name to the appropriate Revit parameter on all descendant devices.
        /// </summary>
        private void OnTopologyConfigAssigned(TopologyNodeViewModel vm)
        {
            if (vm == null) return;

            string paramName = null;
            if (vm.NodeType == "Panel")
                paramName = _activeSettings?.GetRevitParameterName(FireAlarmParameterKeys.PanelConfig);
            else if (vm.NodeType == "Loop")
                paramName = _activeSettings?.GetRevitParameterName(FireAlarmParameterKeys.LoopModuleConfig);

            if (string.IsNullOrEmpty(paramName)) return;

            var configName = vm.AssignedConfig ?? string.Empty;

            System.Collections.Generic.List<(long, string, string)> writes;
            string writeTarget;

            if (vm.NodeType == "Panel")
            {
                // Panel config → write to the panel board element itself.
                // Fall back to descendant devices if no panel element was resolved.
                if (vm.GraphNode.RevitElementId.HasValue)
                {
                    writes = new System.Collections.Generic.List<(long, string, string)>
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
                // Loop module config → always write to all devices in the loop,
                // same as wire assignment. The circuit element (RevitElementId) is
                // a lookup helper and does not carry the loop-config parameter.
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

            _writeParamHandler.Writes = writes;

            _writeParamHandler.OnCompleted = count =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Config '{configName}' written to {writeTarget} ({paramName})."
                        : $"Write succeeded but 0 elements updated — check that '{paramName}' exists as a writable string parameter on the {writeTarget}.");

            _writeParamHandler.OnError = ex =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write config: {ex.Message}");

            _writeParamEvent.Raise();
        }

        /// <summary>
        /// Called when the user assigns a wire type to a loop in the topology tree.
        /// Writes the wire name to the configured Revit parameter on all descendant devices.
        /// </summary>
        private void OnTopologyWireAssigned(TopologyNodeViewModel vm)
        {
            if (vm == null) return;

            string paramName = _activeSettings?.GetRevitParameterName(FireAlarmParameterKeys.Wire);
            if (string.IsNullOrEmpty(paramName)) return;

            var wireName = vm.AssignedWire ?? string.Empty;

            if (vm.DescendantDeviceElementIds.Count == 0)
            {
                StatusText = $"Wire '{wireName}' not written — no device elements resolved for '{vm.Label}'.";
                return;
            }

            _writeParamHandler.Writes = vm.DescendantDeviceElementIds
                .Select(id => (id, paramName, wireName))
                .ToList();

            _writeParamHandler.OnCompleted = count =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Wire '{wireName}' written to {count} device(s) ({paramName})."
                        : $"Write succeeded but 0 elements updated — check that '{paramName}' exists as a writable string parameter on devices.");

            _writeParamHandler.OnError = ex =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write wire: {ex.Message}");

            _writeParamEvent.Raise();

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

            if (_currentData == null) return;

            string paramName = _activeSettings?.GetRevitParameterName(
                Modules.FireAlarm.FireAlarmParameterKeys.Wire);
            if (string.IsNullOrEmpty(paramName)) return;

            // Find the loop by display name
            var loop = _currentData.Loops.Find(l =>
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

            _writeParamHandler.Writes = elementIds
                .Select(id => (id, paramName, wireName ?? string.Empty))
                .ToList();

            _writeParamHandler.OnCompleted = count =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = count > 0
                        ? $"Wire '{wireName}' written to {count} devices in loop '{loopName}' ({paramName})."
                        : $"Wire '{wireName}' saved but 0 elements updated — check that '{paramName}' exists as a writable string parameter.");

            _writeParamHandler.OnError = ex =>
                Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Could not write wire: {ex.Message}");

            _writeParamEvent.Raise();
        }

        /// <summary>Open the Settings dialog for the active module.</summary>
        private void ExecuteOpenSettings()
        {
            var defaults = _activeModule.GetDefaultSettings();
            var settingsVm = new SettingsViewModel(
                _activeSettings, defaults,
                _activeModule.DisplayName,
                _activeModule.Description);

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
            _activeSettings = newSettings;
            StatusText = "Settings applied. Press Refresh to reload data.";

            _saveSettingsHandler.Settings = newSettings;
            _saveSettingsHandler.OnSaved = () =>
                Application.Current?.Dispatcher?.Invoke(() => StatusText = "Settings saved to document.");
            _saveSettingsHandler.OnError = ex =>
                Application.Current?.Dispatcher?.Invoke(() => StatusText = $"Could not save settings: {ex.Message}");

            _saveSettingsEvent.Raise();
        }    }
}