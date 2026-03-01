using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Pulse.Core.Modules;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Revit-layer implementation of ICollectorContext.
    /// Extracts elements and parameter values from the active Revit document.
    /// </summary>
    public class RevitCollectorService : ICollectorContext
    {
        private readonly Document _doc;

        public RevitCollectorService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Collect all family instances from the specified category and extract the requested parameters.
        /// </summary>
        public IReadOnlyList<ElementData> GetElements(string categoryName, IReadOnlyList<string> parameterNames)
        {
            var results = new List<ElementData>();

            // Resolve the BuiltInCategory from the category name
            Category category = FindCategory(categoryName);
            if (category == null)
            {
                return results;
            }

            var collector = new FilteredElementCollector(_doc)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                var data = new ElementData
                {
                    ElementId = element.Id.Value
                };

                // Always expose the element's built-in Name (family type name) so
                // matchers can use it without requiring a configured parameter.
                data.Parameters["_Name"] = element.Name ?? string.Empty;

                // Expose the Revit family name so BOQ and other consumers can display it.
                if (element is FamilyInstance fiFamilyInst)
                    data.Parameters["_FamilyName"] = fiFamilyInst.Symbol?.Family?.Name ?? string.Empty;

                // Inject the elevation (feet) and name of the element's host level,
                // plus the element's own "Elevation from Level" offset parameter.
                if (element is FamilyInstance fi && fi.LevelId != ElementId.InvalidElementId)
                {
                    if (_doc.GetElement(fi.LevelId) is Level hostLevel)
                    {
                        data.Parameters["_LevelElevation"] =
                            hostLevel.Elevation.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        data.Parameters["_LevelName"] = hostLevel.Name ?? string.Empty;
                    }

                    // "Elevation from Level" – try several built-in candidates so that
                    // floor-based, wall-based and face-based families are all handled.
                    Parameter offsetParam =
                        fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)
                        ?? fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM)
                        ?? fi.LookupParameter("Elevation from Level");
                    if (offsetParam != null && offsetParam.StorageType == StorageType.Double)
                        data.Parameters["_ElevationFromLevel"] =
                            offsetParam.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Inject the element's XYZ location point for cable-length calculations.
                if (element.Location is LocationPoint locPoint)
                {
                    XYZ pt = locPoint.Point;
                    data.Parameters["_LocationX"] = pt.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    data.Parameters["_LocationY"] = pt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    data.Parameters["_LocationZ"] = pt.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                foreach (string paramName in parameterNames)
                {
                    string value = GetParameterValue(element, paramName);
                    data.Parameters[paramName] = value ?? string.Empty;
                }

                results.Add(data);
            }

            return results;
        }

        /// <summary>
        /// Find a Revit category by display name.
        /// </summary>
        private Category FindCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return null;
            }

            var categories = _doc.Settings.Categories;
            foreach (Category cat in categories)
            {
                if (string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return cat;
                }
            }

            return null;
        }

        /// <summary>
        /// Get all Levels defined in the project, ordered by elevation ascending.
        /// </summary>
        public IReadOnlyList<LevelInfo> GetLevels()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new LevelInfo
                {
                    Name      = l.Name,
                    Elevation = l.Elevation
                })
                .ToList();
        }

        /// <summary>
        /// Extract a parameter value from an element as a string.
        /// Checks instance parameters first, then type parameters.
        /// </summary>
        private static string GetParameterValue(Element element, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return null;
            }

            // Try instance parameter
            Parameter param = element.LookupParameter(parameterName);
            if (param != null && param.HasValue)
            {
                return GetParameterAsString(param);
            }

            // Try type parameter
            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element typeElement = element.Document.GetElement(typeId);
                if (typeElement != null)
                {
                    Parameter typeParam = typeElement.LookupParameter(parameterName);
                    if (typeParam != null && typeParam.HasValue)
                    {
                        return GetParameterAsString(typeParam);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Convert a Revit parameter value to string regardless of storage type.
        /// </summary>
        private static string GetParameterAsString(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString();
                case StorageType.Integer:
                    return param.AsInteger().ToString();
                case StorageType.Double:
                    return param.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    return param.AsElementId().Value.ToString();
                default:
                    return param.AsValueString();
            }
        }
    }
}
