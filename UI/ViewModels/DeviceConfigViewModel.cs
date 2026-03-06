using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Pulse.Core.Settings;
using Pulse.Modules.FireAlarm;

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

        private int _maxAddresses;
        public int MaxAddresses
        {
            get => _maxAddresses;
            set => SetField(ref _maxAddresses, value);
        }

        private double _batteryUnitAh;
        public double BatteryUnitAh
        {
            get => _batteryUnitAh;
            set => SetField(ref _batteryUnitAh, value);
        }

        private double _psuOutputCurrentA;
        public double PsuOutputCurrentA
        {
            get => _psuOutputCurrentA;
            set => SetField(ref _psuOutputCurrentA, value);
        }

        private double _requiredStandbyHours;
        public double RequiredStandbyHours
        {
            get => _requiredStandbyHours;
            set => SetField(ref _requiredStandbyHours, value);
        }

        private double _requiredAlarmMinutes;
        public double RequiredAlarmMinutes
        {
            get => _requiredAlarmMinutes;
            set => SetField(ref _requiredAlarmMinutes, value);
        }

        private double _batterySafetyFactor;
        public double BatterySafetyFactor
        {
            get => _batterySafetyFactor;
            set => SetField(ref _batterySafetyFactor, value);
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
            _maxAddresses = model.MaxAddresses;
            _batteryUnitAh = model.BatteryUnitAh;
            _psuOutputCurrentA = model.PsuOutputCurrentA;
            _requiredStandbyHours = model.RequiredStandbyHours;
            _requiredAlarmMinutes = model.RequiredAlarmMinutes;
            _batterySafetyFactor = model.BatterySafetyFactor;
        }

        public ControlPanelConfig ToModel() => new ControlPanelConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            PanelAddresses = PanelAddresses ?? string.Empty,
            AddressesPerLoop = AddressesPerLoop,
            MaxLoopCount = MaxLoopCount,
            MaxMaPerLoop = MaxMaPerLoop,
            MaxAddresses = MaxAddresses,
            BatteryUnitAh = BatteryUnitAh,
            PsuOutputCurrentA = PsuOutputCurrentA,
            RequiredStandbyHours = RequiredStandbyHours,
            RequiredAlarmMinutes = RequiredAlarmMinutes,
            BatterySafetyFactor = BatterySafetyFactor
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

        private double _resistancePerMetreOhm;
        public double ResistancePerMetreOhm
        {
            get => _resistancePerMetreOhm;
            set => SetField(ref _resistancePerMetreOhm, value);
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

        private string _fireResistance;
        public string FireResistance
        {
            get => _fireResistance;
            set => SetField(ref _fireResistance, value);
        }

        public WireConfigViewModel(WireConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _coreCount = model.CoreCount;
            _coreSizeMm2 = model.CoreSizeMm2;
            _resistancePerMetreOhm = model.ResistancePerMetreOhm;
            _color = model.Color;
            _hasShielding = model.HasShielding;
            _fireResistance = model.FireResistance;
        }

        public WireConfig ToModel() => new WireConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            CoreCount = CoreCount,
            CoreSizeMm2 = CoreSizeMm2,
            ResistancePerMetreOhm = ResistancePerMetreOhm,
            Color = Color ?? string.Empty,
            HasShielding = HasShielding,
            FireResistance = FireResistance ?? string.Empty
        };
    }

    /// <summary>
    /// Editable ViewModel for a single <see cref="PaperSizeConfig"/>.
    /// </summary>
    public class PaperSizeConfigViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private double _widthMm;
        public double WidthMm
        {
            get => _widthMm;
            set => SetField(ref _widthMm, value);
        }

        private double _heightMm;
        public double HeightMm
        {
            get => _heightMm;
            set => SetField(ref _heightMm, value);
        }

        private double _marginLeftMm;
        public double MarginLeftMm
        {
            get => _marginLeftMm;
            set => SetField(ref _marginLeftMm, value);
        }

        private double _marginTopMm;
        public double MarginTopMm
        {
            get => _marginTopMm;
            set => SetField(ref _marginTopMm, value);
        }

        private double _marginRightMm;
        public double MarginRightMm
        {
            get => _marginRightMm;
            set => SetField(ref _marginRightMm, value);
        }

        private double _marginBottomMm;
        public double MarginBottomMm
        {
            get => _marginBottomMm;
            set => SetField(ref _marginBottomMm, value);
        }

        public PaperSizeConfigViewModel(PaperSizeConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _widthMm = model.WidthMm;
            _heightMm = model.HeightMm;
            _marginLeftMm   = model.MarginLeftMm;
            _marginTopMm    = model.MarginTopMm;
            _marginRightMm  = model.MarginRightMm;
            _marginBottomMm = model.MarginBottomMm;
        }

        public PaperSizeConfig ToModel() => new PaperSizeConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            MarginLeftMm   = MarginLeftMm,
            MarginTopMm    = MarginTopMm,
            MarginRightMm  = MarginRightMm,
            MarginBottomMm = MarginBottomMm
        };
    }

    /// <summary>
    /// Editable ViewModel for a single <see cref="PsuConfig"/>.
    /// </summary>
    public class PsuConfigViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private double _voltageV;
        public double VoltageV
        {
            get => _voltageV;
            set => SetField(ref _voltageV, value);
        }

        private double _batteryUnitAh;
        public double BatteryUnitAh
        {
            get => _batteryUnitAh;
            set => SetField(ref _batteryUnitAh, value);
        }

        private double _psuOutputCurrentA;
        public double PsuOutputCurrentA
        {
            get => _psuOutputCurrentA;
            set => SetField(ref _psuOutputCurrentA, value);
        }

        private double _requiredStandbyHours;
        public double RequiredStandbyHours
        {
            get => _requiredStandbyHours;
            set => SetField(ref _requiredStandbyHours, value);
        }

        private double _requiredAlarmMinutes;
        public double RequiredAlarmMinutes
        {
            get => _requiredAlarmMinutes;
            set => SetField(ref _requiredAlarmMinutes, value);
        }

        private double _batterySafetyFactor;
        public double BatterySafetyFactor
        {
            get => _batterySafetyFactor;
            set => SetField(ref _batterySafetyFactor, value);
        }

        public PsuConfigViewModel(PsuConfig model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Id = model.Id;
            _name = model.Name;
            _voltageV = model.VoltageV;
            _batteryUnitAh = model.BatteryUnitAh;
            _psuOutputCurrentA = model.OutputCurrentA;
            _requiredStandbyHours = model.RequiredStandbyHours;
            _requiredAlarmMinutes = model.RequiredAlarmMinutes;
            _batterySafetyFactor = model.BatterySafetyFactor;
        }

        public PsuConfig ToModel() => new PsuConfig
        {
            Id = Id,
            Name = Name ?? string.Empty,
            VoltageV = VoltageV,
            BatteryUnitAh = BatteryUnitAh,
            OutputCurrentA = PsuOutputCurrentA,
            RequiredStandbyHours = RequiredStandbyHours,
            RequiredAlarmMinutes = RequiredAlarmMinutes,
            BatterySafetyFactor = BatterySafetyFactor
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

        public ObservableCollection<PaperSizeConfigViewModel> PaperSizes { get; }
            = new ObservableCollection<PaperSizeConfigViewModel>();

        public ObservableCollection<PsuConfigViewModel> PsuUnits { get; }
            = new ObservableCollection<PsuConfigViewModel>();

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

        private PaperSizeConfigViewModel _selectedPaperSize;
        public PaperSizeConfigViewModel SelectedPaperSize
        {
            get => _selectedPaperSize;
            set => SetField(ref _selectedPaperSize, value);
        }

        private PsuConfigViewModel _selectedPsuUnit;
        public PsuConfigViewModel SelectedPsuUnit
        {
            get => _selectedPsuUnit;
            set => SetField(ref _selectedPsuUnit, value);
        }

        // ─── Tab state ───────────────────────────────────────────────────────
        private int _activeTab;
        /// <summary>0 = Control Panels, 1 = Loop Modules, 2 = Wires, 3 = Paper, 4 = PSU Units.</summary>
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
                    OnPropertyChanged(nameof(IsPaperSizesTabActive));
                    OnPropertyChanged(nameof(IsPsuUnitsTabActive));
                }
            }
        }

        public bool IsPanelsTabActive      => _activeTab == 0;
        public bool IsLoopModulesTabActive => _activeTab == 1;
        public bool IsWiresTabActive       => _activeTab == 2;
        public bool IsPaperSizesTabActive  => _activeTab == 3;
        public bool IsPsuUnitsTabActive    => _activeTab == 4;

        // ─── Commands ────────────────────────────────────────────────────────
        public ICommand SelectPanelsTabCommand { get; }
        public ICommand SelectLoopModulesTabCommand { get; }
        public ICommand SelectWiresTabCommand { get; }
        public ICommand SelectPaperSizesTabCommand { get; }
        public ICommand SelectPsuUnitsTabCommand { get; }

        public ICommand AddPanelCommand { get; }
        public ICommand RemovePanelCommand { get; }
        public ICommand AddLoopModuleCommand { get; }
        public ICommand RemoveLoopModuleCommand { get; }
        public ICommand AddWireCommand { get; }
        public ICommand RemoveWireCommand { get; }

        public ICommand AddPaperSizeCommand { get; }
        public ICommand RemovePaperSizeCommand { get; }

        public ICommand AddPsuUnitCommand { get; }
        public ICommand RemovePsuUnitCommand { get; }

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

            foreach (var ps in store.PaperSizes)
                PaperSizes.Add(new PaperSizeConfigViewModel(ps));

            var faConfig = DeviceConfigService.LoadModuleConfig<FireAlarmDeviceConfig>("FireAlarm");
            foreach (var psu in faConfig.PsuUnits)
                PsuUnits.Add(new PsuConfigViewModel(psu));

            // Tab commands
            SelectPanelsTabCommand      = new RelayCommand(_ => ActiveTab = 0);
            SelectLoopModulesTabCommand = new RelayCommand(_ => ActiveTab = 1);
            SelectWiresTabCommand       = new RelayCommand(_ => ActiveTab = 2);
            SelectPaperSizesTabCommand  = new RelayCommand(_ => ActiveTab = 3);
            SelectPsuUnitsTabCommand    = new RelayCommand(_ => ActiveTab = 4);

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

            // Paper size CRUD
            AddPaperSizeCommand = new RelayCommand(_ =>
            {
                var vm = new PaperSizeConfigViewModel(new PaperSizeConfig
                    { Name = $"Paper Size {PaperSizes.Count + 1}" });
                PaperSizes.Add(vm);
                SelectedPaperSize = vm;
            });

            RemovePaperSizeCommand = new RelayCommand(_ =>
            {
                if (SelectedPaperSize == null) return;
                PaperSizes.Remove(SelectedPaperSize);
                SelectedPaperSize = PaperSizes.Count > 0 ? PaperSizes[0] : null;
            });

            // PSU unit CRUD
            AddPsuUnitCommand = new RelayCommand(_ =>
            {
                var vm = new PsuConfigViewModel(new PsuConfig
                    { Name = $"PSU {PsuUnits.Count + 1}", VoltageV = 24.0, RequiredStandbyHours = 24.0, RequiredAlarmMinutes = 30.0, BatterySafetyFactor = 1.25 });
                PsuUnits.Add(vm);
                SelectedPsuUnit = vm;
            });

            RemovePsuUnitCommand = new RelayCommand(_ =>
            {
                if (SelectedPsuUnit == null) return;
                PsuUnits.Remove(SelectedPsuUnit);
                SelectedPsuUnit = PsuUnits.Count > 0 ? PsuUnits[0] : null;
            });

            // Save / Cancel
            SaveCommand   = new RelayCommand(_ => ExecuteSave());
            CancelCommand = new RelayCommand(_ => Cancelled?.Invoke());

            // Select first items
            if (Panels.Count > 0)      SelectedPanel      = Panels[0];
            if (LoopModules.Count > 0) SelectedLoopModule = LoopModules[0];
            if (Wires.Count > 0)       SelectedWire       = Wires[0];
            if (PaperSizes.Count > 0)  SelectedPaperSize  = PaperSizes[0];
            if (PsuUnits.Count > 0)    SelectedPsuUnit    = PsuUnits[0];
        }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private void ExecuteSave()
        {
            // Load the existing library store so previously saved wire/panel/loop
            // type definitions are preserved — we only overwrite the sections we manage here.
            // Per-document assignments are stored separately in Extensible Storage.
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

            store.PaperSizes.Clear();
            foreach (var vm in PaperSizes)
                store.PaperSizes.Add(vm.ToModel());

            DeviceConfigService.Save(store);

            // Save PSU units into the FireAlarm module config blob
            var faConfig = DeviceConfigService.LoadModuleConfig<FireAlarmDeviceConfig>("FireAlarm");
            faConfig.PsuUnits.Clear();
            foreach (var vm in PsuUnits)
                faConfig.PsuUnits.Add(vm.ToModel());
            DeviceConfigService.SaveModuleConfig("FireAlarm", faConfig);

            Saved?.Invoke();
        }
    }
}
