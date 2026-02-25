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
                .SetToolTip("Open the Pulse Fire Alarm module.")
                .SetLongDescription("Pulse is a modern MEP UX platform for addressable systems. Manage fire alarm panels, loops, zones, and devices with real-time topology visualization.");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
