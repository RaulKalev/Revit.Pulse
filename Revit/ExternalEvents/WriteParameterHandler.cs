using System;
using System.Collections.Generic;
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

                        // Try instance parameter first; fall back to the element type for type parameters.
                        Parameter param = element.LookupParameter(paramName);
                        if (param == null || param.IsReadOnly)
                        {
                            var typeId = element.GetTypeId();
                            if (typeId != null && typeId != ElementId.InvalidElementId)
                            {
                                var typeElem = doc.GetElement(typeId);
                                if (typeElem != null)
                                    param = typeElem.LookupParameter(paramName);
                            }
                        }

                        if (param == null || param.IsReadOnly || param.StorageType != StorageType.String)
                            continue;

                        param.Set(value ?? string.Empty);
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
    }
}
