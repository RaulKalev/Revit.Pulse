using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using ricaun.Revit.UI.Utils;
using Pulse.Commands;

namespace Pulse
{
    /// <summary>
    /// Revit application entry point for the Pulse platform.
    /// Registers the ribbon buttons that launch the main Pulse window.
    /// </summary>
    [AppLoader]
    public class App : IExternalApplication
    {
        private PushButton _fireAlarmButton;
        private PushButton _lightingButton;

        private const string FireUri    = "pack://application:,,,/Pulse;component/Assets/{0}%20-%20Pulse%20-%20Fire.tiff";
        private const string LightingUri = "pack://application:,,,/Pulse;component/Assets/{0}%20-%20Pulse%20-%20Lighting.tiff";

        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "RK Tools";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists
            }

            var panel = application.CreateOrSelectPanel(tabName, "Pulse");

            string variant = RibbonThemeUtils.IsDark ? "Dark" : "Light";

            _fireAlarmButton = panel.CreatePushButton<PulseFireAlarm>()
                .SetLargeImage(string.Format(FireUri, variant))
                .SetText("Fire\nAlarm")
                .SetToolTip("Open Pulse – Fire Alarm Module\n\n• Get a clear overview of your fire alarm system\n• View panels, loops, and devices in one place\n• Review loads, capacity, cable lengths, and device distribution\n• Spot issues early during design\n• Generate information for diagrams, documentation, and system review")
                .SetContextualHelp("https://raulkalev.github.io/rktools/pulse/index.html");

            _lightingButton = panel.CreatePushButton<PulseLighting>()
                .SetLargeImage(string.Format(LightingUri, variant))
                .SetText("Pulse\nLighting")
                .SetToolTip("Open Pulse – Lighting Module\n\n• Manage addressable lighting controllers, lines, and luminaires\n• Review address and current capacity per DALI line\n• Spot over-capacity and missing assignments early during design\n• View topology, inspector, and metrics in one place\n• Supports DALI and future lighting systems")
                .SetContextualHelp("https://raulkalev.github.io/rktools/pulse/index.html");

            RibbonThemeUtils.ThemeChanged += OnThemeChanged;

            return Result.Succeeded;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            string variant = e.IsDark ? "Dark" : "Light";
            _fireAlarmButton?.SetLargeImage(string.Format(FireUri, variant));
            _lightingButton?.SetLargeImage(string.Format(LightingUri, variant));
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            RibbonThemeUtils.ThemeChanged -= OnThemeChanged;
            return Result.Succeeded;
        }
    }
}
