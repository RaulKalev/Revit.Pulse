using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Runtime.InteropServices;

namespace Pulse.Commands
{
    /// <summary>
    /// Manages the singleton Pulse main window across all module launch commands.
    /// If the window is already open, brings it to front and switches module.
    /// If not, creates a new window and activates the requested module.
    /// </summary>
    internal static class PulseWindowManager
    {
        private static UI.MainWindow _window;

        /// <summary>
        /// Retained when the window closes so we can flush its in-memory assignments to
        /// Extensible Storage synchronously on the next Execute call (Revit API thread),
        /// before the new window reads ES back.
        /// </summary>
        private static UI.ViewModels.MainViewModel _lastViewModel;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        /// <summary>
        /// Open the Pulse window (or bring existing to front) and activate the given module.
        /// </summary>
        /// <param name="commandData">Revit command data.</param>
        /// <param name="moduleId">Module ID to activate (e.g. "FireAlarm", "Lighting").</param>
        /// <param name="message">Error message string ref.</param>
        public static Result OpenOrFocus(ExternalCommandData commandData, string moduleId, ref string message)
        {
            try
            {
                // If window already exists, bring it to front and switch module
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

                    // Switch module if different from current
                    _window.ViewModel?.SwitchModule(moduleId);

                    return Result.Succeeded;
                }

                // Create new Pulse window
                UIApplication uiApp = commandData.Application;

                // Flush any in-memory state from the previous session synchronously
                if (_lastViewModel != null)
                {
                    var doc = uiApp.ActiveUIDocument?.Document;
                    if (doc != null)
                        _lastViewModel.FlushPendingToRevit(doc);
                    _lastViewModel = null;
                }

                _window = new UI.MainWindow(uiApp, moduleId);
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
