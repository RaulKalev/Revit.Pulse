using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Pulse.Core;
using Pulse.Modules.Lighting;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Lighting Device Manager window.
    /// Manages the catalog of DALI controllers and power supplies stored in
    /// %APPDATA%\Pulse\lighting-devices.json.
    /// </summary>
    public class LightingDeviceManagerViewModel : ViewModelBase
    {
        private readonly LightingDeviceDatabaseService _service;

        // ── Observable device list ────────────────────────────────────────

        public ObservableCollection<LightingDeviceDto> Devices { get; }
            = new ObservableCollection<LightingDeviceDto>();

        private LightingDeviceDto _selectedDevice;
        public LightingDeviceDto SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetField(ref _selectedDevice, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelection => _selectedDevice != null;

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand AddControllerCommand    { get; }
        public ICommand AddPowerSupplyCommand   { get; }
        public ICommand RemoveDeviceCommand     { get; }
        public ICommand MoveUpCommand           { get; }
        public ICommand MoveDownCommand         { get; }
        public ICommand SaveCommand             { get; }
        public ICommand CancelCommand           { get; }

        // ── Events (raised to close the window) ───────────────────────────

        public event Action Saved;
        public event Action Cancelled;

        // ── Constructor ───────────────────────────────────────────────────

        public LightingDeviceManagerViewModel(LightingDeviceDatabaseService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            // Populate list from current catalog
            foreach (var d in _service.Load().Devices)
                Devices.Add(d);

            AddControllerCommand = new RelayCommand(_ =>
            {
                var dto = new LightingDeviceDto
                {
                    Id                      = Guid.NewGuid().ToString(),
                    Type                    = "controller",
                    Manufacturer            = "New",
                    Model                   = "Controller",
                    DaliLines               = 1,
                    MaxAddressesPerLine     = 64,
                    RatedCurrentMaPerLine   = 250,
                    GuaranteedCurrentMaPerLine = 200
                };
                Devices.Add(dto);
                SelectedDevice = dto;
            });

            AddPowerSupplyCommand = new RelayCommand(_ =>
            {
                var dto = new LightingDeviceDto
                {
                    Id                      = Guid.NewGuid().ToString(),
                    Type                    = "power_supply",
                    Manufacturer            = "New",
                    Model                   = "PSU",
                    DaliLines               = 1,
                    MaxAddressesPerLine     = 64,
                    RatedCurrentMaPerLine   = 250,
                    GuaranteedCurrentMaPerLine = 200
                };
                Devices.Add(dto);
                SelectedDevice = dto;
            });

            RemoveDeviceCommand = new RelayCommand(
                _ =>
                {
                    if (_selectedDevice == null) return;
                    int idx = Devices.IndexOf(_selectedDevice);
                    Devices.Remove(_selectedDevice);
                    SelectedDevice = Devices.Count > 0
                        ? Devices[Math.Min(idx, Devices.Count - 1)]
                        : null;
                },
                _ => _selectedDevice != null);

            MoveUpCommand = new RelayCommand(
                _ =>
                {
                    int idx = Devices.IndexOf(_selectedDevice);
                    if (idx <= 0) return;
                    Devices.Move(idx, idx - 1);
                },
                _ => _selectedDevice != null && Devices.IndexOf(_selectedDevice) > 0);

            MoveDownCommand = new RelayCommand(
                _ =>
                {
                    int idx = Devices.IndexOf(_selectedDevice);
                    if (idx < 0 || idx >= Devices.Count - 1) return;
                    Devices.Move(idx, idx + 1);
                },
                _ => _selectedDevice != null && Devices.IndexOf(_selectedDevice) < Devices.Count - 1);

            SaveCommand = new RelayCommand(_ =>
            {
                var db = _service.Load();
                db.Devices.Clear();
                foreach (var d in Devices)
                    db.Devices.Add(d);
                _service.Save();
                Saved?.Invoke();
            });

            CancelCommand = new RelayCommand(_ => Cancelled?.Invoke());
        }
    }
}
