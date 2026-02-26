using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that writes a string value to a named parameter
    /// on a set of Revit elements. Used to stamp device-config assignments.
    /// </summary>
    public class WriteParameterHandler : IExternalEventHandler
    {
        /// <summary>
        /// List of writes to perform: (elementId, parameterName, value).
        /// Set this before raising the event.
        /// </summary>
        public List<(long ElementId, string ParameterName, string Value)> Writes { get; set; }

        /// <summary>Callback invoked (on the UI thread) after a successful write.</summary>
        public Action<int> OnCompleted { get; set; }

        /// <summary>Callback invoked if the write fails.</summary>
        public Action<Exception> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    OnError?.Invoke(new InvalidOperationException("No active Revit document."));
                    return;
                }

                if (Writes == null || Writes.Count == 0)
                {
                    OnCompleted?.Invoke(0);
                    return;
                }

                int count = 0;
                using (var tx = new Transaction(doc, "Pulse: Write Config Parameter"))
                {
                    tx.Start();

                    foreach (var (elementId, paramName, value) in Writes)
                    {
                        var element = doc.GetElement(new ElementId(elementId));
                        if (element == null) continue;

                        // Try instance parameter first, then the element type.
                        // We always try both because LookupParameter on an instance can
                        // return a type parameter that appears writable but whose Set()
                        // has no effect when called via the instance — the type element
                        // must be used directly for true type parameters.
                        if (TryWriteParam(element, paramName, value))
                        {
                            count++;
                            continue;
                        }

                        var typeId = element.GetTypeId();
                        if (typeId == null || typeId == ElementId.InvalidElementId) continue;

                        var typeElem = doc.GetElement(typeId);
                        if (typeElem == null) continue;

                        if (TryWriteParam(typeElem, paramName, value))
                            count++;
                    }

                    tx.Commit();
                }

                OnCompleted?.Invoke(count);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pulse] WriteParameterHandler error: {ex.Message}");
                OnError?.Invoke(ex);
            }
        }

        public string GetName() => "Pulse.WriteParameter";

        /// <summary>
        /// Writes a string value to a parameter whose storage type is String, Double, or Integer.
        /// For Double/Integer the value string is parsed; an unparseable string writes 0.
        /// </summary>
        private static bool TryWriteParam(Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;

            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value ?? string.Empty);
                    return true;

                case StorageType.Double:
                    double d = 0;
                    if (!string.IsNullOrWhiteSpace(value))
                        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out d);
                    param.Set(d);
                    return true;

                case StorageType.Integer:
                    int i = 0;
                    if (!string.IsNullOrWhiteSpace(value))
                        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out i);
                    param.Set(i);
                    return true;

                default:
                    return false;
            }
        }
    }
}
