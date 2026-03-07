using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using Pulse.Commands;

namespace Pulse
{
    /// <summary>
    /// Revit application entry point for the Pulse platform.
    /// Registers the ribbon button that launches the main Pulse window.
    /// </summary>
    [AppLoader]
    public class App : IExternalApplication
    {
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

            panel.CreatePushButton<PulseFireAlarm>()
                .SetLargeImage("pack://application:,,,/Pulse;component/Assets/Light%20-%20Pulse%20-%20Fire.tiff")
                .SetText("Fire Alarm")
                .SetToolTip("Open Pulse – Fire Alarm Module\n\n• Get a clear overview of your fire alarm system\n• View panels, loops, and devices in one place\n• Review loads, capacity, cable lengths, and device distribution\n• Spot issues early during design\n• Generate information for diagrams, documentation, and system review")
                .SetContextualHelp("https://raulkalev.github.io/rktools/pulse/index.html");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
