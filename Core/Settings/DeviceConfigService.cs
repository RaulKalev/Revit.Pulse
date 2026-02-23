using System;
using System.IO;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Reads and writes the <see cref="DeviceConfigStore"/> to/from a local JSON file
    /// at <c>%APPDATA%\Pulse\device-config.json</c>.
    /// </summary>
    public static class DeviceConfigService
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "device-config.json");

        /// <summary>
        /// Load the device configuration from disk.
        /// Returns an empty <see cref="DeviceConfigStore"/> if the file does not exist or cannot be read.
        /// </summary>
        public static DeviceConfigStore Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    return JsonConvert.DeserializeObject<DeviceConfigStore>(json)
                           ?? new DeviceConfigStore();
                }
            }
            catch
            {
                // Fall through — return fresh store
            }

            return new DeviceConfigStore();
        }

        /// <summary>
        /// Persist the device configuration to disk, creating the directory if needed.
        /// </summary>
        public static void Save(DeviceConfigStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(store, Formatting.Indented);
            File.WriteAllText(StorePath, json);
        }
    }
}
