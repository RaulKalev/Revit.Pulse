using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Pulse.Revit.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that draws (or clears) model lines in Revit to
    /// visualise the Manhattan cable routing for fire alarm loops.
    ///
    /// Each loop's lines live in a dedicated line-style subcategory
    /// ("Pulse Wire – {loopKey}") so they can be toggled independently.
    /// </summary>
    public class DrawWireRoutingHandler : IExternalEventHandler
    {
        /// <summary>
        /// Ordered waypoints for a single loop (in Revit internal units — feet).
        /// Set this before raising the event.
        /// </summary>
        public List<(double X, double Y, double Z)> Waypoints { get; set; }

        /// <summary>
        /// Composite key identifying the loop ("panelName::loopName").
        /// Used to name the per-loop line style subcategory.
        /// </summary>
        public string LoopKey { get; set; }

        /// <summary>When true, only delete the lines for <see cref="LoopKey"/> without creating new ones.</summary>
        public bool ClearOnly { get; set; }

        /// <summary>Callback on success with count of lines drawn (negative = cleared).</summary>
        public Action<int> OnCompleted { get; set; }

        /// <summary>Callback on failure.</summary>
        public Action<Exception> OnError { get; set; }

        private const string TransactionName = "Pulse: Wire Routing";

        /// <summary>Prefix for per-loop line style subcategory names.</summary>
        private const string LineStylePrefix = "Pulse Wire – ";

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

                if (string.IsNullOrEmpty(LoopKey))
                {
                    OnError?.Invoke(new InvalidOperationException("LoopKey must be set."));
                    return;
                }

                // Revit prohibits  \ : { } [ ] ; < > ? ` ~  in category names.
                string safeKey = LoopKey
                    .Replace("::", " - ")
                    .Replace("\\", "_").Replace(":", "_")
                    .Replace("{", "_").Replace("}", "_")
                    .Replace("[", "_").Replace("]", "_")
                    .Replace(";", "_").Replace("<", "_")
                    .Replace(">", "_").Replace("?", "_")
                    .Replace("`", "_").Replace("~", "_");
                string subCatName = LineStylePrefix + safeKey;

                using (var tx = new Transaction(doc, TransactionName))
                {
                    tx.Start();

                    // 1. Delete existing lines for this loop.
                    int deleted = DeleteLinesBySubcategory(doc, subCatName);

                    if (ClearOnly || Waypoints == null || Waypoints.Count < 2)
                    {
                        tx.Commit();
                        OnCompleted?.Invoke(deleted > 0 ? -deleted : 0);
                        return;
                    }

                    // 2. Resolve or create the per-loop line style.
                    GraphicsStyle lineStyle = GetOrCreateLineStyle(doc, subCatName);

                    // 3. Draw Manhattan-routed model lines.
                    int linesDrawn = 0;
                    for (int i = 1; i < Waypoints.Count; i++)
                    {
                        linesDrawn += DrawManhattanSegment(doc, Waypoints[i - 1], Waypoints[i], lineStyle);
                    }

                    tx.Commit();
                    OnCompleted?.Invoke(linesDrawn);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        // ── Drawing helpers ─────────────────────────────────────────────────

        private static int DrawManhattanSegment(
            Document doc,
            (double X, double Y, double Z) from,
            (double X, double Y, double Z) to,
            GraphicsStyle lineStyle)
        {
            int count = 0;
            var p0 = new XYZ(from.X, from.Y, from.Z);
            var p1 = new XYZ(to.X,   from.Y, from.Z);
            var p2 = new XYZ(to.X,   to.Y,   from.Z);
            var p3 = new XYZ(to.X,   to.Y,   to.Z);

            count += TryCreateModelLine(doc, p0, p1, lineStyle);
            count += TryCreateModelLine(doc, p1, p2, lineStyle);
            count += TryCreateModelLine(doc, p2, p3, lineStyle);
            return count;
        }

        private static int TryCreateModelLine(Document doc, XYZ start, XYZ end, GraphicsStyle lineStyle)
        {
            const double tolerance = 0.005;
            if (start.DistanceTo(end) < tolerance)
                return 0;

            Plane plane;
            XYZ dir = (end - start).Normalize();
            if (Math.Abs(dir.Z) > 0.999)
                plane = Plane.CreateByNormalAndOrigin(XYZ.BasisX, start);
            else
            {
                XYZ normal = dir.CrossProduct(XYZ.BasisZ);
                if (normal.GetLength() < 1e-9) normal = XYZ.BasisY;
                else normal = normal.Normalize();
                plane = Plane.CreateByNormalAndOrigin(normal, start);
            }

            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
            Line line = Line.CreateBound(start, end);
            ModelLine ml = doc.Create.NewModelCurve(line, sketchPlane) as ModelLine;
            if (ml != null && lineStyle != null)
                ml.LineStyle = lineStyle;
            return ml != null ? 1 : 0;
        }

        // ── Subcategory helpers ──────────────────────────────────────────────

        /// <summary>Delete all model lines using a specific subcategory name.</summary>
        private static int DeleteLinesBySubcategory(Document doc, string subCatName)
        {
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return 0;

            Category subCat = null;
            foreach (Category sub in linesCat.SubCategories)
                if (sub.Name == subCatName) { subCat = sub; break; }
            if (subCat == null) return 0;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WhereElementIsNotElementType();

            var toDelete = new List<ElementId>();
            foreach (Element elem in collector)
            {
                if (elem is ModelLine ml)
                {
                    var gs = ml.LineStyle as GraphicsStyle;
                    if (gs?.GraphicsStyleCategory?.Name == subCatName)
                        toDelete.Add(ml.Id);
                }
            }

            if (toDelete.Count > 0)
                doc.Delete(toDelete.ToList());
            return toDelete.Count;
        }

        /// <summary>Delete all model lines whose subcategory name starts with the Pulse prefix.</summary>
        public static int DeleteAllRoutingLines(Document doc)
        {
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return 0;

            var matchingSubs = new HashSet<string>(StringComparer.Ordinal);
            foreach (Category sub in linesCat.SubCategories)
                if (sub.Name.StartsWith(LineStylePrefix, StringComparison.Ordinal))
                    matchingSubs.Add(sub.Name);
            if (matchingSubs.Count == 0) return 0;

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WhereElementIsNotElementType();

            var toDelete = new List<ElementId>();
            foreach (Element elem in collector)
            {
                if (elem is ModelLine ml)
                {
                    var gs = ml.LineStyle as GraphicsStyle;
                    if (gs?.GraphicsStyleCategory != null && matchingSubs.Contains(gs.GraphicsStyleCategory.Name))
                        toDelete.Add(ml.Id);
                }
            }

            if (toDelete.Count > 0)
                doc.Delete(toDelete.ToList());
            return toDelete.Count;
        }

        private static GraphicsStyle GetOrCreateLineStyle(Document doc, string subCatName)
        {
            Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return null;

            Category subCat = null;
            foreach (Category sub in linesCat.SubCategories)
                if (sub.Name == subCatName) { subCat = sub; break; }

            if (subCat == null)
            {
                subCat = doc.Settings.Categories.NewSubcategory(linesCat, subCatName);
                subCat.LineColor = new Color(255, 60, 60);
                subCat.SetLineWeight(3, GraphicsStyleType.Projection);
            }

            return subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        public string GetName() => "Pulse: Draw Wire Routing";
    }
}
