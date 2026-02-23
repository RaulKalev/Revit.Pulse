using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Core.Settings;
using Pulse.Core.Graph;
using Pulse.Modules.FireAlarm;
using Pulse.Revit.ExternalEvents;

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

        // Child ViewModels
        public TopologyViewModel Topology { get; }
        public InspectorViewModel Inspector { get; }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand ResetOverridesCommand { get; }
        public ICommand FilterWarningsCommand { get; }

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

            // Wire up topology selection events
            Topology.NodeSelected += OnTopologyNodeSelected;

            // Create ExternalEvent handlers
            _collectHandler = new CollectDevicesHandler();
            _collectEvent = ExternalEvent.Create(_collectHandler);

            _selectHandler = new SelectElementHandler();
            _selectEvent = ExternalEvent.Create(_selectHandler);

            _overrideHandler = new TemporaryOverrideHandler();
            _overrideEvent = ExternalEvent.Create(_overrideHandler);

            _resetHandler = new ResetOverridesHandler();
            _resetEvent = ExternalEvent.Create(_resetHandler);

            // Create commands
            RefreshCommand = new RelayCommand(ExecuteRefresh, () => !IsLoading);
            ResetOverridesCommand = new RelayCommand(ExecuteResetOverrides);
            FilterWarningsCommand = new RelayCommand(_ => ShowWarningsOnly = !ShowWarningsOnly);
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

            // Update statistics
            TotalDevices = data.Devices.Count;
            TotalWarnings = data.WarningCount;
            TotalErrors = data.ErrorCount;

            // Update topology
            Topology.LoadFromModuleData(data);

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
                _selectEvent.Raise();
            }
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
    }
}
