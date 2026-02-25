using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Runtime.InteropServices;

namespace Pulse.Commands
{
    /// <summary>
    /// External command that launches the main Pulse window.
    /// Implements singleton window pattern — re-surfaces existing window if already open.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PulseFireAlarm : IExternalCommand
    {
        private static UI.MainWindow _window;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // If window already exists, bring it to front
                if (_window != null && _window.IsLoaded)
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                    if (_window.WindowState == System.Windows.WindowState.Minimized)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                    }

                    _window.Activate();
                    _window.Focus();
                    SetForegroundWindow(hwnd);
                    return Result.Succeeded;
                }

                // Create new Pulse window
                UIApplication uiApp = commandData.Application;

                _window = new UI.MainWindow(uiApp);
                var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                new System.Windows.Interop.WindowInteropHelper(_window) { Owner = owner };

                _window.Closed += (s, e) => { _window = null; };
                _window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
