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

        /// <summary>
        /// Retained when the window closes so we can flush its in-memory assignments to
        /// Extensible Storage synchronously on the next Execute call (Revit API thread),
        /// before the new window reads ES back. This avoids the race where async
        /// ExternalEvent saves haven't fired yet when LoadInitialSettings reads ES.
        /// </summary>
        private static UI.ViewModels.MainViewModel _lastViewModel;

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

                // Flush any in-memory state from the previous session synchronously
                // (we are on the Revit API thread here, so ES writes are safe).
                // This ensures LoadInitialSettings in the new window reads fresh data
                // even if the async ExternalEvent saves haven't fired yet.
                if (_lastViewModel != null)
                {
                    var doc = uiApp.ActiveUIDocument?.Document;
                    if (doc != null)
                        _lastViewModel.FlushPendingToRevit(doc);
                    _lastViewModel = null;
                }

                _window = new UI.MainWindow(uiApp);
                var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                new System.Windows.Interop.WindowInteropHelper(_window) { Owner = owner };

                _window.Closed += (s, e) =>
                {
                    _lastViewModel = _window?.ViewModel;
                    _window = null;
                };
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
