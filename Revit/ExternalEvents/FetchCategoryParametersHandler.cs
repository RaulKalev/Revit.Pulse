using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;
using Pulse.Revit.Services;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that scans the first N elements of a Revit category
    /// and returns all unique parameter names found on them.
    /// Used to populate the "Available in Revit" list in the BOQ parameter picker.
    /// </summary>
    public class FetchCategoryParametersHandler : IExternalEventHandler
    {
        /// <summary>Revit category name to scan (e.g. "Fire Alarm Devices").</summary>
        public string CategoryName { get; set; }

        /// <summary>Invoked on the UI thread with the sorted list of discovered parameter names.</summary>
        public Action<IReadOnlyList<string>> OnCompleted { get; set; }

        /// <summary>Invoked on the UI thread if the scan fails.</summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    var err = new InvalidOperationException("No active Revit document.");
                    Application.Current?.Dispatcher?.BeginInvoke((Action)(() => OnError?.Invoke(err)));
                    return;
                }

                var service = new RevitCollectorService(doc);
                var keys = service.GetAllParameterNames(CategoryName ?? string.Empty);

                Application.Current?.Dispatcher?.BeginInvoke((Action)(() => OnCompleted?.Invoke(keys)));
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.BeginInvoke((Action)(() => OnError?.Invoke(ex)));
            }
        }

        public string GetName() => "Pulse: Fetch Category Parameters";
    }
}
