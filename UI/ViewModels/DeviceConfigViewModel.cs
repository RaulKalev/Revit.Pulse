using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Pulse.Core.Settings;

namespace Pulse.UI.ViewModels
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Per-item ViewModels
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Editable ViewModel for a single <see cref="ControlPanelConfig"/>.
    /// </summary>
    public class ControlPanelConfigViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private string _panelAddresses;
        public string PanelAddresses
        {
            get => _panelAddresses;
            set => SetField(ref _panelAddresses, value);
        }

        private int _addressesPerLoop;
        public int AddressesPerLoop
        {
            get => _addressesPerLoop;
            set => SetField(ref _addressesPerLoop, value);
        }

        private int _maxLoopCount;
        public int MaxLoopCount
        {
            get => _maxLoopCount;
            set => SetField(ref _maxLoopCount, value);
        }

        private double _maxMaPerLoop;
        public double MaxMaPerLoop
        {
            get => _maxMaPerLoop;
            set => SetField(ref _maxMaPerLoop, value);
        }

        public ControlPanelConfigViewModel(ControlPanelConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _panelAddresses = model.PanelAddresses;
            _addressesPerLoop = model.AddressesPerLoop;
            _maxLoopCount = model.MaxLoopCount;
            _maxMaPerLoop = model.MaxMaPerLoop;
        }

        public ControlPanelConfig ToModel() => new ControlPanelConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            PanelAddresses = PanelAddresses ?? string.Empty,
            AddressesPerLoop = AddressesPerLoop,
            MaxLoopCount = MaxLoopCount,
            MaxMaPerLoop = MaxMaPerLoop
        };
    }

    /// <summary>
    /// Editable ViewModel for a single <see cref="LoopModuleConfig"/>.
    /// </summary>
    public class LoopModuleConfigViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private string _panelAddresses;
        public string PanelAddresses
        {
            get => _panelAddresses;
            set => SetField(ref _panelAddresses, value);
        }

        private int _addressesPerLoop;
        public int AddressesPerLoop
        {
            get => _addressesPerLoop;
            set => SetField(ref _addressesPerLoop, value);
        }

        private int _maxLoopCount;
        public int MaxLoopCount
        {
            get => _maxLoopCount;
            set => SetField(ref _maxLoopCount, value);
        }

        private double _maxMaPerLoop;
        public double MaxMaPerLoop
        {
            get => _maxMaPerLoop;
            set => SetField(ref _maxMaPerLoop, value);
        }

        public LoopModuleConfigViewModel(LoopModuleConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _panelAddresses = model.PanelAddresses;
            _addressesPerLoop = model.AddressesPerLoop;
            _maxLoopCount = model.MaxLoopCount;
            _maxMaPerLoop = model.MaxMaPerLoop;
        }

        public LoopModuleConfig ToModel() => new LoopModuleConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            PanelAddresses = PanelAddresses ?? string.Empty,
            AddressesPerLoop = AddressesPerLoop,
            MaxLoopCount = MaxLoopCount,
            MaxMaPerLoop = MaxMaPerLoop
        };
    }

    /// <summary>
    /// Editable ViewModel for a single <see cref="WireConfig"/>.
    /// </summary>
    public class WireConfigViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private int _coreCount;
        public int CoreCount
        {
            get => _coreCount;
            set => SetField(ref _coreCount, value);
        }

        private double _coreSizeMm2;
        public double CoreSizeMm2
        {
            get => _coreSizeMm2;
            set => SetField(ref _coreSizeMm2, value);
        }

        private string _color;
        public string Color
        {
            get => _color;
            set => SetField(ref _color, value);
        }

        private bool _hasShielding;
        public bool HasShielding
        {
            get => _hasShielding;
            set => SetField(ref _hasShielding, value);
        }

        private bool _isFireResistant;
        public bool IsFireResistant
        {
            get => _isFireResistant;
            set => SetField(ref _isFireResistant, value);
        }

        public WireConfigViewModel(WireConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _coreCount = model.CoreCount;
            _coreSizeMm2 = model.CoreSizeMm2;
            _color = model.Color;
            _hasShielding = model.HasShielding;
            _isFireResistant = model.IsFireResistant;
        }

        public WireConfig ToModel() => new WireConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            CoreCount = CoreCount,
            CoreSizeMm2 = CoreSizeMm2,
            Color = Color ?? string.Empty,
            HasShielding = HasShielding,
            IsFireResistant = IsFireResistant
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Root DeviceConfigViewModel
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ViewModel for the Device Configurator window.
    /// Manages control panel, loop module, and wire libraries persisted to local JSON.
    /// </summary>
    public class DeviceConfigViewModel : ViewModelBase
    {
        // ─── Collections ─────────────────────────────────────────────────────
        public ObservableCollection<ControlPanelConfigViewModel> Panels { get; }
            = new ObservableCollection<ControlPanelConfigViewModel>();

        public ObservableCollection<LoopModuleConfigViewModel> LoopModules { get; }
            = new ObservableCollection<LoopModuleConfigViewModel>();

        public ObservableCollection<WireConfigViewModel> Wires { get; }
            = new ObservableCollection<WireConfigViewModel>();

        // ─── Selection ───────────────────────────────────────────────────────
        private ControlPanelConfigViewModel _selectedPanel;
        public ControlPanelConfigViewModel SelectedPanel
        {
            get => _selectedPanel;
            set => SetField(ref _selectedPanel, value);
        }

        private LoopModuleConfigViewModel _selectedLoopModule;
        public LoopModuleConfigViewModel SelectedLoopModule
        {
            get => _selectedLoopModule;
            set => SetField(ref _selectedLoopModule, value);
        }

        private WireConfigViewModel _selectedWire;
        public WireConfigViewModel SelectedWire
        {
            get => _selectedWire;
            set => SetField(ref _selectedWire, value);
        }

        // ─── Tab state ───────────────────────────────────────────────────────
        private int _activeTab;
        /// <summary>0 = Control Panels, 1 = Loop Modules, 2 = Wires.</summary>
        public int ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetField(ref _activeTab, value))
                {
                    OnPropertyChanged(nameof(IsPanelsTabActive));
                    OnPropertyChanged(nameof(IsLoopModulesTabActive));
                    OnPropertyChanged(nameof(IsWiresTabActive));
                }
            }
        }

        public bool IsPanelsTabActive      => _activeTab == 0;
        public bool IsLoopModulesTabActive => _activeTab == 1;
        public bool IsWiresTabActive       => _activeTab == 2;

        // ─── Commands ────────────────────────────────────────────────────────
        public ICommand SelectPanelsTabCommand { get; }
        public ICommand SelectLoopModulesTabCommand { get; }
        public ICommand SelectWiresTabCommand { get; }

        public ICommand AddPanelCommand { get; }
        public ICommand RemovePanelCommand { get; }
        public ICommand AddLoopModuleCommand { get; }
        public ICommand RemoveLoopModuleCommand { get; }
        public ICommand AddWireCommand { get; }
        public ICommand RemoveWireCommand { get; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        // ─── Events ──────────────────────────────────────────────────────────
        public event Action Saved;
        public event Action Cancelled;

        // ─── Constructor ─────────────────────────────────────────────────────
        public DeviceConfigViewModel()
        {
            var store = DeviceConfigService.Load();

            foreach (var p in store.ControlPanels)
                Panels.Add(new ControlPanelConfigViewModel(p));

            foreach (var m in store.LoopModules)
                LoopModules.Add(new LoopModuleConfigViewModel(m));

            foreach (var w in store.Wires)
                Wires.Add(new WireConfigViewModel(w));

            // Tab commands
            SelectPanelsTabCommand      = new RelayCommand(_ => ActiveTab = 0);
            SelectLoopModulesTabCommand = new RelayCommand(_ => ActiveTab = 1);
            SelectWiresTabCommand       = new RelayCommand(_ => ActiveTab = 2);

            // Panel CRUD
            AddPanelCommand = new RelayCommand(_ =>
            {
                var vm = new ControlPanelConfigViewModel(new ControlPanelConfig
                    { Name = $"Panel {Panels.Count + 1}" });
                Panels.Add(vm);
                SelectedPanel = vm;
            });

            RemovePanelCommand = new RelayCommand(_ =>
            {
                if (SelectedPanel == null) return;
                Panels.Remove(SelectedPanel);
                SelectedPanel = Panels.Count > 0 ? Panels[0] : null;
            });

            // Loop module CRUD
            AddLoopModuleCommand = new RelayCommand(_ =>
            {
                var vm = new LoopModuleConfigViewModel(new LoopModuleConfig
                    { Name = $"Loop Module {LoopModules.Count + 1}" });
                LoopModules.Add(vm);
                SelectedLoopModule = vm;
            });

            RemoveLoopModuleCommand = new RelayCommand(_ =>
            {
                if (SelectedLoopModule == null) return;
                LoopModules.Remove(SelectedLoopModule);
                SelectedLoopModule = LoopModules.Count > 0 ? LoopModules[0] : null;
            });

            // Wire CRUD
            AddWireCommand = new RelayCommand(_ =>
            {
                var vm = new WireConfigViewModel(new WireConfig
                    { Name = $"Wire {Wires.Count + 1}" });
                Wires.Add(vm);
                SelectedWire = vm;
            });

            RemoveWireCommand = new RelayCommand(_ =>
            {
                if (SelectedWire == null) return;
                Wires.Remove(SelectedWire);
                SelectedWire = Wires.Count > 0 ? Wires[0] : null;
            });

            // Save / Cancel
            SaveCommand   = new RelayCommand(_ => ExecuteSave());
            CancelCommand = new RelayCommand(_ => Cancelled?.Invoke());

            // Select first items
            if (Panels.Count > 0)     SelectedPanel     = Panels[0];
            if (LoopModules.Count > 0) SelectedLoopModule = LoopModules[0];
            if (Wires.Count > 0)       SelectedWire       = Wires[0];
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private void ExecuteSave()
        {
            // Load the existing store so that non-UI data (assignments, flip states, etc.)
            // is preserved — we only overwrite the sections we manage here.
            var store = DeviceConfigService.Load();

            store.ControlPanels.Clear();
            foreach (var vm in Panels)
                store.ControlPanels.Add(vm.ToModel());

            store.LoopModules.Clear();
            foreach (var vm in LoopModules)
                store.LoopModules.Add(vm.ToModel());

            store.Wires.Clear();
            foreach (var vm in Wires)
                store.Wires.Add(vm.ToModel());

            DeviceConfigService.Save(store);
            Saved?.Invoke();
        }
    }
}
