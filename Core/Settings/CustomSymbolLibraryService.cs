using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Reads and writes the user's custom symbol library to
    /// <c>%APPDATA%\Pulse\custom-symbols.json</c>.
    /// </summary>
    public static class CustomSymbolLibraryService
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "custom-symbols.json");

        /// <summary>
        /// Load all custom symbol definitions from disk.
        /// Returns an empty list if the file does not exist or cannot be parsed.
        /// </summary>
        public static List<CustomSymbolDefinition> Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    return JsonConvert.DeserializeObject<List<CustomSymbolDefinition>>(json)
                           ?? new List<CustomSymbolDefinition>();
                }
            }
            catch
            {
                // Fall through
            }

            return new List<CustomSymbolDefinition>();
        }

        /// <summary>
        /// Persist the full symbol library to disk, creating the directory if needed.
        /// </summary>
        public static void Save(IEnumerable<CustomSymbolDefinition> library)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));

            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(library, Formatting.Indented);
            File.WriteAllText(StorePath, json);
        }
    }
}
