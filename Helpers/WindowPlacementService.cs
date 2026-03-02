using System;
using System.IO;
using Newtonsoft.Json;

namespace Pulse.Helpers
{
    /// <summary>
    /// Persists and restores window position and size across Revit sessions.
    /// Data is stored in %APPDATA%\Pulse\window_placement.json.
    /// </summary>
    public static class WindowPlacementService
    {
        private static readonly string _appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse");

        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pulse",
            "window_placement.json");

        // ── Generic file-keyed overloads (used by auxiliary windows) ─────────

        /// <summary>
        /// Save position + size for any window identified by <paramref name="fileName"/>.
        /// The file is written alongside the main window_placement.json in %APPDATA%\Pulse\.
        /// </summary>
        public static void Save(double left, double top, double width, double height, string fileName)
        {
            try
            {
                var data = new PlacementData { Left = left, Top = top, Width = width, Height = height };
                var path = Path.Combine(_appDir, fileName);
                Directory.CreateDirectory(_appDir);
                File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch { /* Non-critical */ }
        }

        /// <summary>
        /// Load position + size from <paramref name="fileName"/>.
        /// Returns null when the file does not exist or data is unusable.
        /// </summary>
        public static PlacementData Load(string fileName)
        {
            try
            {
                var path = Path.Combine(_appDir, fileName);
                if (!File.Exists(path)) return null;
                var data = JsonConvert.DeserializeObject<PlacementData>(File.ReadAllText(path));
                if (data == null || data.Width < 100 || data.Height < 100) return null;
                return data;
            }
            catch { return null; }
        }

        public static void Save(double left, double top, double width, double height,
            double diagramPanelWidth = 300,
            bool metricsPanelExpanded = false,
            double metricsPanelHeight = 150)
        {
            try
            {
                var data = new PlacementData
                {
                    Left                 = left,
                    Top                  = top,
                    Width                = width,
                    Height               = height,
                    DiagramPanelWidth    = diagramPanelWidth,
                    MetricsPanelExpanded = metricsPanelExpanded,
                    MetricsPanelHeight   = metricsPanelHeight,
                };

                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch
            {
                // Non-critical — silently ignore save failures
            }
        }

        /// <summary>
        /// Returns saved placement, or null if none exists or data is invalid.
        /// </summary>
        public static PlacementData Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                var data = JsonConvert.DeserializeObject<PlacementData>(File.ReadAllText(_filePath));

                // Basic sanity check — reject unusable values
                if (data == null || data.Width < 100 || data.Height < 100) return null;

                return data;
            }
            catch
            {
                return null;
            }
        }

        public class PlacementData
        {
            public double Left                 { get; set; }
            public double Top                  { get; set; }
            public double Width                { get; set; }
            public double Height               { get; set; }
            public double DiagramPanelWidth    { get; set; } = 300;
            public bool   MetricsPanelExpanded { get; set; } = false;
            public double MetricsPanelHeight   { get; set; } = 150;
        }
    }
}
