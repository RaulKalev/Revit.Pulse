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
                .SetToolTip("Pulse — Fire Alarm Module")
                .SetLongDescription(
                    "Pulse is a modern fire alarm engineering tool for Revit.\n\n" +
                    "• Build panel → loop → device topology with V-drop analysis\n" +
                    "• Size batteries and PSUs using the EN 54-4 formula\n" +
                    "• Draw and measure wire routing on the model\n" +
                    "• Generate a Bill of Quantities with CSV export\n" +
                    "• Run an AI-assisted system health check\n\n" +
                    "Press F1 for full documentation.")
                .SetContextualHelp("https://raulkalev.github.io/rktools/pulse/index.html");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
