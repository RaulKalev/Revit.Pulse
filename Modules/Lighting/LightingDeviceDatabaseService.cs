using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Pulse.Modules.Lighting
{
    /// <summary>
    /// Loads and saves the DALI hardware device catalog from
    /// <c>%APPDATA%\Pulse\lighting-devices.json</c>.
    /// Creates the file with default Helvar entries on first run.
    /// </summary>
    public sealed class LightingDeviceDatabaseService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "lighting-devices.json");

        private LightingDeviceDatabase _db;

        /// <summary>Loads the database from disk (or returns cached in-memory copy).</summary>
        public LightingDeviceDatabase Load()
        {
            if (_db != null) return _db;

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    _db = JsonConvert.DeserializeObject<LightingDeviceDatabase>(json);
                }
                catch { /* corrupt file — recreate defaults */ }
            }

            if (_db == null)
            {
                _db = CreateDefaults();
                Save();
            }

            return _db;
        }

        /// <summary>Saves the current in-memory database to disk.</summary>
        public void Save()
        {
            if (_db == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_db, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] LightingDeviceDatabaseService.Save failed: {ex.Message}");
            }
        }

        /// <summary>Returns only the controller-type devices.</summary>
        public List<LightingDeviceDto> GetControllers()
        {
            var db = Load();
            return db.Devices.Where(d => string.Equals(d.Type, "controller",
                StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static LightingDeviceDatabase CreateDefaults()
        {
            return new LightingDeviceDatabase
            {
                Devices = new List<LightingDeviceDto>
                {
                    // Helvar DALI Controllers
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "905",  Type = "controller", DaliLines = 4,  MaxAddressesPerLine = 64, RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "910",  Type = "controller", DaliLines = 16, MaxAddressesPerLine = 64, RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "920",  Type = "controller", DaliLines = 16, MaxAddressesPerLine = 64, RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "945",  Type = "controller", DaliLines = 4,  MaxAddressesPerLine = 64, RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "950",  Type = "controller", DaliLines = 8,  MaxAddressesPerLine = 64, RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    // Helvar DALI Power Supplies
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "402",  Type = "power_supply", DaliLines = 1, MaxAddressesPerLine = 0,  RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                    new LightingDeviceDto { Manufacturer = "Helvar", Model = "407",  Type = "power_supply", DaliLines = 1, MaxAddressesPerLine = 0,  RatedCurrentMaPerLine = 250, GuaranteedCurrentMaPerLine = 200 },
                }
            };
        }
    }
}
