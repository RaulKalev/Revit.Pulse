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
using Pulse.UI;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// Root ViewModel for the Pulse main window.
    /// Owns UI bindings and commands; delegates orchestration to extracted services:
    ///   - <see cref="PulseAppController"/>               — module registry, active module, settings state
    ///   - <see cref="RefreshPipeline"/>                   — collect/build/evaluate via ExternalEvent
    ///   - <see cref="SelectionHighlightFacade"/>          — select/highlight/reset in Revit
    ///   - <see cref="StorageFacade"/>                     — safe ES and JSON persistence
    ///   - <see cref="TopologyAssignmentsService"/>        — per-document assignment lifecycle
    ///   - <see cref="SymbolMappingOrchestrator"/>         — custom symbol library + mapping
    ///   - <see cref="DiagramFeatureService"/>             — diagram wire orchestration
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;

        // ── Orchestration services ───────────────────────────────────────────
        private readonly PulseAppController _appController;
        private readonly RefreshPipeline _refreshPipeline;
        private readonly SelectionHighlightFacade _selectionFacade;
        private readonly StorageFacade _storageFacade;

        // ── Feature services ─────────────────────────────────────────────────
        private readonly TopologyAssignmentsService _assignmentsService;
        private readonly SymbolMappingOrchestrator _symbolOrchestrator;
        private readonly DiagramFeatureService _diagramFeatureService;

        /// <summary>Compatibility shortcut — returns the live store from the assignments service.</summary>
        private TopologyAssignmentsStore _topologyAssignments => _assignmentsService.Store;

        /// <summary>
        /// When set, OnDataCollected will check whether this element ID was collected
        /// and produce a diagnostic status message so the user knows exactly why a
        /// sub-device assignment did or did not appear in the tree.
        /// </summary>
        private long? _pendingSubDeviceElementId;

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

        /// <summary>
        /// Synchronously flush in-memory assignments and diagram visibility directly to
        /// Extensible Storage. Call from the Revit API thread (IExternalCommand.Execute)
        /// before creating a new window — ensures the new instance reads up-to-date data
        /// instead of whatever stale state was last written by an async ExternalEvent.
        /// </summary>
        internal void FlushPendingToRevit(Document doc)
        {
            if (doc == null) return;
            _assignmentsService.FlushToRevit(doc);
            _storageFacade.SyncWriteDiagramSettings(doc, Diagram.Visibility);
        }

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

            // ── Feature services ─────────────────────────────────────────
            _assignmentsService = new TopologyAssignmentsService
            {
                SaveRequested = store => _storageFacade.SaveTopologyAssignments(store),
                SyncWriteRequested = (doc, store) => _storageFacade.SyncWriteTopologyAssignments(doc, store)
            };
            _symbolOrchestrator = new SymbolMappingOrchestrator();
            _diagramFeatureService = new DiagramFeatureService(_appController);

            // Create child ViewModels
            Topology = new TopologyViewModel();
            Inspector = new InspectorViewModel();
            Inspector.CurrentDrawValueCommitted += OnCurrentDrawValueCommitted;
            Topology.SubDeviceAssignRequested      += OnSubDeviceAssignRequested;
            Topology.PickElementForDeviceRequested += OnPickElementForDeviceRequested;
            Topology.SubDeviceRemoveRequested      += OnSubDeviceRemoveRequested;
            Diagram = new DiagramViewModel();

            // Wire up topology selection events
            Topology.NodeSelected     += OnTopologyNodeSelected;

            // Guard config-assignment wiring with capability check
            if (_appController.HasCapability(ModuleCapabilities.ConfigAssignment))
                Topology.ConfigAssigned += OnTopologyConfigAssigned;

            // Guard wire-assignment wiring with capability check
            if (_appController.HasCapability(ModuleCapabilities.Wiring))
                Topology.WireAssigned += OnTopologyWireAssigned;

            // Per-loop wire routing toggle (draw/clear model lines)
            Topology.WireRoutingToggled += OnWireRoutingToggled;

            // Wire diagram visibility saves (guarded by Diagram capability)
            if (_appController.HasCapability(ModuleCapabilities.Diagram))
            {
                Diagram.VisibilityChanged = () =>
                    _storageFacade.SaveDiagramSettings(Diagram.Visibility);
            }

            // Wire topology assignment saves (panel/loop configs, flip states, etc.)
            Action saveAssignments = () => _assignmentsService.RequestSave();
            Topology.AssignmentsSaveRequested = saveAssignments;
            Diagram.AssignmentsSaveRequested = saveAssignments;

            // Wire diagram wire-assignment writes (guarded by Wiring capability)
            if (_appController.HasCapability(ModuleCapabilities.Wiring))
                Diagram.WireAssigned = OnDiagramWireAssigned;

            // Create commands
            RefreshCommand = new RelayCommand(ExecuteRefresh, () => !IsLoading);
            ResetOverridesCommand = new RelayCommand(ExecuteResetOverrides);
            FilterWarningsCommand = new RelayCommand(_ => ShowWarningsOnly = !ShowWarningsOnly);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            OpenDeviceConfiguratorCommand = new RelayCommand(ExecuteOpenDeviceConfigurator);
            OpenSymbolMappingCommand = new RelayCommand(
                ExecuteOpenSymbolMapping,
                () => _appController.HasCapability(ModuleCapabilities.SymbolMapping));

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

            // NOTE: _topologyAssignments is intentionally NOT reloaded from
            // RefreshedAssignments here. The in-memory store is the source of truth:
            // it is loaded from ES once in LoadInitialSettings and kept up to date by
            // every assignment mutation (panel config, loop module, wire, flip, etc.).
            // Overwriting it from an async ES read would lose any assignments the user
            // made between the last save event firing and this refresh completing.

            // Update statistics
            TotalDevices = data.Devices.Count;
            TotalWarnings = data.WarningCount;
            TotalErrors = data.ErrorCount;

            // Update topology (assignments must be set before LoadFromModuleData builds the tree)
            // Capture expand state from live memory before rebuilding (disk copy may be stale).
            var previouslyExpandedIds = Topology.GetExpandedNodeIds();
            Topology.LoadAssignments(_topologyAssignments);
            Topology.LoadFromModuleData(data);
            IEnumerable<string> idsToRestore = previouslyExpandedIds.Count > 0
                ? (IEnumerable<string>)previouslyExpandedIds
                : UiStateService.Load().ExpandedNodeIds;
            Topology.RestoreExpandState(idsToRestore);

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

            // Sub-device assignment diagnostic: tell the user exactly why a pick did/didn't appear.
            if (_pendingSubDeviceElementId.HasValue)
            {
                long checkId = _pendingSubDeviceElementId.Value;
                _pendingSubDeviceElementId = null;

                var found = data.Devices.FirstOrDefault(d => d.RevitElementId == checkId);
                if (found == null)
                {
                    StatusText = $"\u26a0 Element {checkId} was NOT collected after refresh. " +
                                  "Its Revit category is probably not listed in Settings \u2192 Categories. " +
                                  "Add the category and refresh again.";
                }
                else if (string.IsNullOrEmpty(found.Address) || !found.Address.Contains('.'))
                {
                    StatusText = $"\u26a0 Element collected as '{found.DisplayName}' but Address='{found.Address ?? "(none)"}'. " +
                                  "The Address parameter write may have failed — check the shared parameter exists on the family.";
                }
                else
                {
                    // Check what parent the topology builder actually wired it to
                    string loopId = found.LoopId ?? "(none)";
                    var edge = data.Edges.FirstOrDefault(e => e.TargetId == found.EntityId);
                    string parentEntityId = edge?.SourceId ?? "(no edge)";
                    bool parentIsDevice = data.Devices.Any(d => d.EntityId == parentEntityId);
                    string parentLabel = parentIsDevice
                        ? data.Devices.First(d => d.EntityId == parentEntityId).DisplayName
                        : parentEntityId;
                    StatusText = $"\u2139 '{found.DisplayName}' addr='{found.Address}' loopId='{loopId}' → parent='{parentLabel}' (isDevice={parentIsDevice})";
                }
            }

            // Apply current filter
            ApplyFilter();

            // Restore wire routing lines for any loops that were visible before
            RestoreWireRoutingFromStore();

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
                    // Prune stale keys and merge in new defaults.
                    var defaults = _appController.ActiveModule.GetDefaultSettings();
                    var defaultKeySet = new HashSet<string>(
                        defaults.ParameterMappings.ConvertAll(m => m.LogicalName),
                        StringComparer.OrdinalIgnoreCase);
                    // Remove any mappings whose LogicalName no longer exists in defaults.
                    jsonSettings.ParameterMappings.RemoveAll(m => !defaultKeySet.Contains(m.LogicalName));
                    // Add any new defaults not yet present in the stored settings.
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
                _assignmentsService.Load(_storageFacade.ReadTopologyAssignments(doc));
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
            var vm = new SymbolMappingViewModel(_appController.CurrentData, _topologyAssignments.SymbolMappings, _symbolOrchestrator.MutableLibrary);

            vm.Saved += mappings =>
            {
                _symbolOrchestrator.ApplyMappings(mappings, _topologyAssignments);
                _assignmentsService.RequestSave();
            };

            var win = new SymbolMappingWindow(vm)
            {
                Owner = _ownerWindow
            };

            // When the user designs a new symbol, persist it to the library file
            win.SymbolCreated += definition =>
            {
                _symbolOrchestrator.UpsertSymbol(definition);
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
        /// Called when the user commits an inline edit of CurrentDrawNormal or CurrentDrawAlarm.
        /// Looks up the Revit parameter name from settings, writes the value, then refreshes.
        /// </summary>
        private void OnCurrentDrawValueCommitted(long elementId, bool isAlarm, string newValue)
        {
            var settings = _appController.ActiveSettings;
            if (settings == null) return;
            string paramKey  = isAlarm ? FireAlarmParameterKeys.CurrentDrawAlarm : FireAlarmParameterKeys.CurrentDrawNormal;
            string paramName = settings.GetRevitParameterName(paramKey);
            if (string.IsNullOrEmpty(paramName))
            {
                StatusText = $"Cannot write: '{paramKey}' is not mapped to a Revit parameter in Settings.";
                return;
            }

            string label = isAlarm ? "Current draw (alarm)" : "Current draw (normal)";
            StatusText = $"Writing {label}...";

            _storageFacade.WriteParameters(
                new List<(long, string, string)> { (elementId, paramName, newValue) },
                count =>
                {
                    // Use BeginInvoke so this runs AFTER WriteParameterHandler.Execute() has
                    // fully returned — you cannot raise a second ExternalEvent from inside
                    // another ExternalEvent's Execute() callback.
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (count > 0)
                        {
                            StatusText = $"{label} written to Revit.";
                            ExecuteRefresh();
                        }
                        else
                        {
                            StatusText = $"{label}: write succeeded but 0 elements updated \u2014 check '{paramName}' exists on the element.";
                        }
                    }));
                },
                ex =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        StatusText = $"Could not write {label}: {ex.Message}"));
                });
        }

        /// <summary>
        /// Called when the user picks an unassigned device from the add-slot combobox on a device row.
        /// Writes the Loop and Address parameters to Revit, then refreshes.
        /// </summary>
        private void OnSubDeviceAssignRequested(TopologyNodeViewModel hostVm, UnassignedDeviceOption option)
        {
            if (hostVm == null || option == null) return;

            var settings = _appController.ActiveSettings;
            if (settings == null) return;

            string loopParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Loop);
            string addrParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Address);
            string panelParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Panel);

            if (string.IsNullOrEmpty(loopParamName) || string.IsNullOrEmpty(addrParamName))
            {
                StatusText = "Cannot assign sub-device: Loop or Address parameter is not mapped in Settings.";
                return;
            }

            hostVm.GraphNode.Properties.TryGetValue("Loop", out string loopValue);
            hostVm.GraphNode.Properties.TryGetValue("Panel", out string panelValue);
            string newAddress = hostVm.NextSubAddress;

            StatusText = $"Assigning {option.Label} as sub-device…";

            // Force-expand the host device node after rebuild so the new child is visible
            Topology.ScheduleExpand(hostVm.GraphNode.Id);

            _storageFacade.WriteParameters(
                new List<(long, string, string)>
                {
                    (option.ElementId, loopParamName, loopValue ?? string.Empty),
                    (option.ElementId, addrParamName, newAddress),
                    (option.ElementId, panelParamName, panelValue ?? string.Empty),
                },
                count =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (count > 0)
                        {
                            StatusText = $"Sub-device '{option.Label}' → {newAddress} ({count}/3 params written). Refreshing…";
                            _pendingSubDeviceElementId = option.ElementId;
                            hostVm.MarkSubDeviceAdded(option);
                            ExecuteRefresh();
                        }
                        else
                        {
                            StatusText = $"Write failed (0/3 params) — '{option.Label}' does not have Loop/Address/Panel parameters. Add the shared params to its family.";
                        }
                    }));
                },
                ex =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        StatusText = $"Could not assign sub-device: {ex.Message}"));
                });
        }

        /// <summary>
        /// Called when the user clicks "Pick from Revit" on a device row.
        /// Minimises the plugin window, starts a Revit pick session, then writes
        /// Loop + Address when the user selects an element.
        /// </summary>
        private void OnPickElementForDeviceRequested(TopologyNodeViewModel hostVm)
        {
            if (hostVm == null) return;

            var settings = _appController.ActiveSettings;
            if (settings == null) return;

            string loopParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Loop);
            string addrParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Address);
            string panelParamName = settings.GetRevitParameterName(FireAlarmParameterKeys.Panel);

            if (string.IsNullOrEmpty(loopParamName) || string.IsNullOrEmpty(addrParamName))
            {
                StatusText = "Cannot assign sub-device: Loop or Address parameter is not mapped in Settings.";
                return;
            }

            hostVm.GraphNode.Properties.TryGetValue("Loop", out string loopValue);
            hostVm.GraphNode.Properties.TryGetValue("Panel", out string panelPickValue);
            string newAddress = hostVm.NextSubAddress;

            // Minimise the plugin window so the user can click in the Revit viewport
            _ownerWindow?.Dispatcher.Invoke(() =>
            {
                if (_ownerWindow != null)
                    _ownerWindow.WindowState = System.Windows.WindowState.Minimized;
            });

            StatusText = "Pick a device in the Revit viewport…";

            // Force-expand the host device node after rebuild so the new child is visible
            Topology.ScheduleExpand(hostVm.GraphNode.Id);

            _storageFacade.PickElement(
                "Select a device to assign to " + (hostVm.Label ?? "this device"),
                onPicked: pickedElementId =>
                {
                    // Still on the Revit API thread — marshal to UI thread
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        // Restore window
                        if (_ownerWindow != null)
                        {
                            _ownerWindow.WindowState = System.Windows.WindowState.Normal;
                            _ownerWindow.Activate();
                        }

                        StatusText = $"Writing Loop + Address to picked element…";

                        _storageFacade.WriteParameters(
                            new List<(long, string, string)>
                            {
                                (pickedElementId, loopParamName, loopValue ?? string.Empty),
                                (pickedElementId, addrParamName, newAddress),
                                (pickedElementId, panelParamName, panelPickValue ?? string.Empty),
                            },
                            count =>
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    if (count > 0)
                                    {
                                        StatusText = $"Picked element → {newAddress} ({count}/3 params written). Refreshing…";
                                        _pendingSubDeviceElementId = pickedElementId;
                                        hostVm.IncrementSubDeviceCount();
                                        ExecuteRefresh();
                                    }
                                    else
                                    {
                                        StatusText = $"Write failed (0/3 params) — picked element does not have Loop/Address/Panel parameters. Add the shared params to its family.";
                                    }
                                }));
                            },
                            ex =>
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                    StatusText = $"Could not write to picked element: {ex.Message}"));
                            });
                    }));
                },
                onCancelled: () =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (_ownerWindow != null)
                        {
                            _ownerWindow.WindowState = System.Windows.WindowState.Normal;
                            _ownerWindow.Activate();
                        }
                        StatusText = "Pick cancelled.";
                    }));
                },
                onError: ex =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (_ownerWindow != null)
                        {
                            _ownerWindow.WindowState = System.Windows.WindowState.Normal;
                            _ownerWindow.Activate();
                        }
                        StatusText = $"Pick failed: {ex.Message}";
                    }));
                });
        }

        /// <summary>
        /// Called when the user clicks the "-" button on a sub-device row.
        /// Clears Loop and Address params on the element, reverting it to unassigned.
        /// </summary>
        private void OnSubDeviceRemoveRequested(TopologyNodeViewModel subDeviceVm)
        {
            if (subDeviceVm?.GraphNode?.RevitElementId == null) return;

            var settings = _appController.ActiveSettings;
            if (settings == null) return;

            long elementId   = subDeviceVm.GraphNode.RevitElementId.Value;
            string loopParam = settings.GetRevitParameterName(FireAlarmParameterKeys.Loop);
            string addrParam = settings.GetRevitParameterName(FireAlarmParameterKeys.Address);

            StatusText = $"Removing sub-device '{subDeviceVm.Label}'\u2026";

            _storageFacade.WriteParameters(
                new List<(long, string, string)>
                {
                    (elementId, loopParam, string.Empty),
                    (elementId, addrParam, string.Empty),
                },
                count =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        StatusText = count > 0
                            ? $"Sub-device '{subDeviceVm.Label}' removed."
                            : "Remove failed \u2014 Loop/Address params could not be cleared.";
                        ExecuteRefresh();
                    }));
                },
                ex =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        StatusText = $"Could not remove sub-device: {ex.Message}"));
                });
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
        /// Handle per-loop wire routing toggle from the topology view.
        /// Draws or clears model lines for a single loop.
        /// </summary>
        private void OnWireRoutingToggled(TopologyNodeViewModel loopVm)
        {
            string loopKey = (loopVm.ParentLabel ?? string.Empty) + "::" + loopVm.Label;

            if (!loopVm.IsWireRoutingVisible)
            {
                // Clear lines for this loop
                StatusText = $"Clearing wire routing for {loopVm.Label}…";
                _storageFacade.DrawWireRouting(
                    loopKey,
                    waypoints: null,
                    clearOnly: true,
                    onCompleted: count => Application.Current?.Dispatcher?.Invoke(() =>
                        StatusText = $"Wire routing cleared for {loopVm.Label} ({count} lines removed)."),
                    onError: ex => Application.Current?.Dispatcher?.Invoke(() =>
                        StatusText = $"Could not clear wire routing: {ex.Message}"));
                return;
            }

            // Build waypoints for this specific loop
            var waypoints = BuildWaypointsForLoop(loopVm);
            if (waypoints == null || waypoints.Count < 2)
            {
                StatusText = $"No device coordinates for '{loopVm.Label}' (parent='{loopVm.ParentLabel}') — refresh first.";
                // Revert the toggle since we can't draw
                loopVm.SetWireRoutingVisibleSilent(false);
                return;
            }

            StatusText = $"Drawing wire routing for {loopVm.Label} ({waypoints.Count} waypoints)…";
            _storageFacade.DrawWireRouting(
                loopKey,
                waypoints,
                clearOnly: false,
                onCompleted: count => Application.Current?.Dispatcher?.Invoke(() =>
                    StatusText = $"Wire routing for {loopVm.Label}: {count} model line(s) drawn."),
                onError: ex => Application.Current?.Dispatcher?.Invoke(() =>
                {
                    loopVm.SetWireRoutingVisibleSilent(false);
                    StatusText = $"Could not draw wire routing: {ex.Message}";
                }));
        }

        /// <summary>
        /// Build ordered waypoints for a single loop node using its parent panel
        /// and child devices from the current ModuleData.
        /// </summary>
        private List<(double X, double Y, double Z)> BuildWaypointsForLoop(TopologyNodeViewModel loopVm)
        {
            var data = _appController.CurrentData;
            if (data == null) return null;

            // Find the matching panel + loop in the data model
            foreach (var panel in data.Panels)
            {
                if (!string.Equals(panel.DisplayName, loopVm.ParentLabel, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var loop in panel.Loops)
                {
                    if (!string.Equals(loop.DisplayName, loopVm.Label, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var waypoints = new List<(double X, double Y, double Z)>();

                    (double X, double Y, double Z)? panelOrigin = null;
                    if (panel.LocationX.HasValue && panel.LocationY.HasValue && panel.LocationZ.HasValue)
                        panelOrigin = (panel.LocationX.Value, panel.LocationY.Value, panel.LocationZ.Value);

                    if (panelOrigin.HasValue)
                        waypoints.Add(panelOrigin.Value);

                    var ordered = loop.Devices
                        .Where(d => d.LocationX.HasValue && d.LocationY.HasValue && d.LocationZ.HasValue)
                        .OrderBy(d => ParseAddressSortKey(d.Address))
                        .ThenBy(d => d.Address, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var device in ordered)
                        waypoints.Add((device.LocationX.Value, device.LocationY.Value, device.LocationZ.Value));

                    if (panelOrigin.HasValue && waypoints.Count > 1)
                        waypoints.Add(panelOrigin.Value);

                    return waypoints.Count >= 2 ? waypoints : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Restore wire routing model lines for all loops that were marked visible
        /// in Extensible Storage. Called after data reload.
        /// </summary>
        private void RestoreWireRoutingFromStore()
        {
            if (_topologyAssignments.LoopWireRoutingVisible.Count == 0) return;

            var data = _appController.CurrentData;
            if (data == null) return;

            // Find every loop node whose key appears in the store and redraw its wires
            foreach (var panelVm in Topology.RootNodes)
            {
                foreach (var loopVm in panelVm.Children.Where(c => c.NodeType == "Loop"))
                {
                    string key = (loopVm.ParentLabel ?? string.Empty) + "::" + loopVm.Label;
                    if (_topologyAssignments.LoopWireRoutingVisible.TryGetValue(key, out bool vis) && vis)
                    {
                        var waypoints = BuildWaypointsForLoop(loopVm);
                        if (waypoints != null && waypoints.Count >= 2)
                        {
                            _storageFacade.DrawWireRouting(key, waypoints, clearOnly: false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extract a numeric sort key from a device address string for routing order.
        /// </summary>
        private static int ParseAddressSortKey(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return int.MaxValue;
            if (int.TryParse(address, out int intVal))
                return intVal;
            for (int i = address.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(address[i]))
                {
                    string tail = address.Substring(i + 1);
                    if (tail.Length > 0 && int.TryParse(tail, out int tailVal))
                        return tailVal;
                    break;
                }
            }
            return int.MaxValue;
        }

        /// <summary>
        /// Called when the user confirms new settings in the dialog.
        /// Updates the active settings in memory and persists them asynchronously.
        /// </summary>
        private void OnSettingsSaved(ModuleSettings newSettings)
        {
            _appController.ApplySettings(newSettings);

            // Auto-refresh so the new parameter mappings take effect immediately.
            ExecuteRefresh();

            _storageFacade.SaveSettings(
                newSettings,
                () => Application.Current?.Dispatcher?.Invoke(() => StatusText = "Settings saved to document."),
                ex => Application.Current?.Dispatcher?.Invoke(() => StatusText = $"Could not save settings: {ex.Message}"));
        }
    }
}