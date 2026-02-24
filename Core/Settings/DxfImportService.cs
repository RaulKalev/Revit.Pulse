using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Parses a DXF (Drawing Exchange Format) ASCII file and converts supported
    /// geometric entities into <see cref="SymbolElement"/> objects.
    /// Supports: LINE, LWPOLYLINE, POLYLINE, CIRCLE, ARC, SPLINE.
    /// All coordinates are normalized to fit the target canvas size.
    /// </summary>
    public static class DxfImportService
    {
        private const double ArcSegments    = 48;   // segments used to approximate arcs
        private const string DefaultStroke  = "#FFFFFF";
        private const double DefaultThickMm = 0.5;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Imports entities from a DXF file and returns them as
        /// <see cref="SymbolElement"/> objects whose coordinates are normalized
        /// to fit within <paramref name="targetWidthMm"/> × <paramref name="targetHeightMm"/>.
        /// </summary>
        public static List<SymbolElement> Import(string filePath,
                                                  double targetWidthMm  = 20.0,
                                                  double targetHeightMm = 20.0)
        {
            if (!File.Exists(filePath)) return new List<SymbolElement>();

            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return new List<SymbolElement>(); }

            var pairs = BuildPairs(lines);
            var raw   = ExtractEntities(pairs);
            return Normalize(raw, targetWidthMm, targetHeightMm);
        }

        // ─── Group-code pair building ─────────────────────────────────────────

        private static List<(int code, string value)> BuildPairs(string[] lines)
        {
            var result = new List<(int, string)>(lines.Length / 2);
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                if (int.TryParse(lines[i].Trim(), out int code))
                    result.Add((code, lines[i + 1].Trim()));
            }
            return result;
        }

        // ─── Entity extraction ────────────────────────────────────────────────

        private static List<SymbolElement> ExtractEntities(List<(int code, string value)> pairs)
        {
            var elements     = new List<SymbolElement>();
            bool inEntities  = false;
            int  i           = 0;

            while (i < pairs.Count)
            {
                var (code, val) = pairs[i];

                if (code == 2 && val == "ENTITIES") { inEntities = true;  i++; continue; }
                if (code == 0 && val == "ENDSEC")   { inEntities = false; i++; continue; }

                if (inEntities && code == 0)
                {
                    switch (val)
                    {
                        case "LINE":       { var el = ParseLine      (pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "LWPOLYLINE": { var el = ParseLwPolyline(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "POLYLINE":   { var el = ParsePolyline  (pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "CIRCLE":     { var el = ParseCircle    (pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "ARC":        { var el = ParseArc       (pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "SPLINE":     { var el = ParseSpline    (pairs, ref i); if (el != null) elements.Add(el); continue; }
                    }
                }
                i++;
            }

            return elements;
        }

        // ─── Entity parsers ───────────────────────────────────────────────────

        private static SymbolElement ParseLine(List<(int code, string value)> p, ref int i)
        {
            i++; // skip entity-name code-0 row
            double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: x1 = D(p[i].value); break;
                    case 20: y1 = D(p[i].value); break;
                    case 11: x2 = D(p[i].value); break;
                    case 21: y2 = D(p[i].value); break;
                }
                i++;
            }
            if (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9) return null;
            return new SymbolElement
            {
                Type              = SymbolElementType.Line,
                Points            = new List<SymbolPoint> { SP(x1, y1), SP(x2, y2) },
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        private static SymbolElement ParseLwPolyline(List<(int code, string value)> p, ref int i)
        {
            i++;
            bool   closed = false;
            var    pts    = new List<SymbolPoint>();
            double cx = 0, cy = 0;
            bool   hasX   = false;

            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 70: closed = (int.Parse(p[i].value) & 1) != 0; break;
                    case 10:
                        if (hasX) pts.Add(SP(cx, cy)); // flush previous vertex
                        cx = D(p[i].value); hasX = true;
                        break;
                    case 20:
                        cy = D(p[i].value);
                        break;
                }
                i++;
            }
            if (hasX) pts.Add(SP(cx, cy)); // flush last vertex

            if (pts.Count < 2) return null;
            return new SymbolElement
            {
                Type              = SymbolElementType.Polyline,
                Points            = pts,
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm,
                IsClosed          = closed
            };
        }

        private static SymbolElement ParsePolyline(List<(int code, string value)> p, ref int i)
        {
            // Older-style POLYLINE; vertices follow as VERTEX entities, terminated by SEQEND
            i++;
            bool closed = false;
            while (i < p.Count && p[i].code != 0)
            {
                if (p[i].code == 70) closed = (int.Parse(p[i].value) & 1) != 0;
                i++;
            }

            var pts = new List<SymbolPoint>();
            while (i < p.Count)
            {
                if (p[i].code == 0 && p[i].value == "VERTEX")
                {
                    i++;
                    double vx = 0, vy = 0;
                    while (i < p.Count && p[i].code != 0)
                    {
                        switch (p[i].code)
                        {
                            case 10: vx = D(p[i].value); break;
                            case 20: vy = D(p[i].value); break;
                        }
                        i++;
                    }
                    pts.Add(SP(vx, vy));
                }
                else if (p[i].code == 0 && p[i].value == "SEQEND") { i++; break; }
                else break;
            }

            if (pts.Count < 2) return null;
            return new SymbolElement
            {
                Type              = SymbolElementType.Polyline,
                Points            = pts,
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm,
                IsClosed          = closed
            };
        }

        private static SymbolElement ParseCircle(List<(int code, string value)> p, ref int i)
        {
            i++;
            double cx = 0, cy = 0, r = 0;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: cx = D(p[i].value); break;
                    case 20: cy = D(p[i].value); break;
                    case 40: r  = D(p[i].value); break;
                }
                i++;
            }
            if (r < 1e-9) return null;
            return new SymbolElement
            {
                Type              = SymbolElementType.Circle,
                Points            = new List<SymbolPoint> { SP(cx, cy), SP(cx + r, cy) },
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        private static SymbolElement ParseArc(List<(int code, string value)> p, ref int i)
        {
            i++;
            double cx = 0, cy = 0, r = 0, startDeg = 0, endDeg = 360;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: cx       = D(p[i].value); break;
                    case 20: cy       = D(p[i].value); break;
                    case 40: r        = D(p[i].value); break;
                    case 50: startDeg = D(p[i].value); break;
                    case 51: endDeg   = D(p[i].value); break;
                }
                i++;
            }
            if (r < 1e-9) return null;

            double span = endDeg - startDeg;
            if (span <= 0) span += 360;
            int segs = Math.Max(4, (int)(ArcSegments * span / 360.0));

            var pts = new List<SymbolPoint>(segs + 1);
            for (int j = 0; j <= segs; j++)
            {
                double a = (startDeg + span * j / segs) * Math.PI / 180.0;
                pts.Add(SP(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
            return new SymbolElement
            {
                Type              = SymbolElementType.Polyline,
                Points            = pts,
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        private static SymbolElement ParseSpline(List<(int code, string value)> p, ref int i)
        {
            // Collect fit/control points (group 10/20) and use them as a polyline
            i++;
            var  pts  = new List<SymbolPoint>();
            while (i < p.Count && p[i].code != 0)
            {
                if (p[i].code == 10 && i + 1 < p.Count && p[i + 1].code == 20)
                {
                    pts.Add(SP(D(p[i].value), D(p[i + 1].value)));
                    i += 2;
                    continue;
                }
                i++;
            }
            if (pts.Count < 2) return null;
            return new SymbolElement
            {
                Type              = SymbolElementType.Polyline,
                Points            = pts,
                StrokeColor       = DefaultStroke,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        // ─── Coordinate normalisation ────────────────────────────────────────

        /// <summary>
        /// Scales and translates all elements to fit in the target canvas.
        /// Also flips Y because DXF Y-axis points up while canvas Y-axis points down.
        /// </summary>
        private static List<SymbolElement> Normalize(List<SymbolElement> elements,
                                                      double targetW, double targetH)
        {
            if (elements.Count == 0) return elements;

            // Bounding box over all raw coordinates
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var el in elements)
                ExpandBbox(el, ref minX, ref minY, ref maxX, ref maxY);

            double srcW = maxX - minX;
            double srcH = maxY - minY;
            if (srcW < 1e-9 && srcH < 1e-9) return elements;

            // Uniform scale with 10 % padding
            const double pad  = 0.10;
            double availW = targetW * (1 - pad * 2);
            double availH = targetH * (1 - pad * 2);

            double scale;
            if      (srcW < 1e-9) scale = availH / srcH;
            else if (srcH < 1e-9) scale = availW / srcW;
            else                  scale = Math.Min(availW / srcW, availH / srcH);

            double offX = targetW * pad - minX * scale;
            double offY = targetH * pad - minY * scale;

            var result = new List<SymbolElement>(elements.Count);
            foreach (var el in elements)
            {
                var newEl = CloneShell(el);
                if (el.Type == SymbolElementType.Circle && el.Points.Count >= 2)
                {
                    double ocx = el.Points[0].X, ocy = el.Points[0].Y;
                    double or_ = Math.Abs(el.Points[1].X - ocx);
                    double ncx = ocx * scale + offX;
                    double ncy = targetH - (ocy * scale + offY); // Y-flip
                    double nr  = or_ * scale;
                    newEl.Points = new List<SymbolPoint> { SP(ncx, ncy), SP(ncx + nr, ncy) };
                }
                else
                {
                    // Y-flip applied here so shapes look correct on canvas
                    newEl.Points = el.Points
                        .Select(p => SP(p.X * scale + offX,
                                        targetH - (p.Y * scale + offY)))
                        .ToList();
                }
                result.Add(newEl);
            }
            return result;
        }

        private static void ExpandBbox(SymbolElement el,
                                        ref double minX, ref double minY,
                                        ref double maxX, ref double maxY)
        {
            if (el.Points == null || el.Points.Count == 0) return;

            if (el.Type == SymbolElementType.Circle && el.Points.Count >= 2)
            {
                double cx = el.Points[0].X, cy = el.Points[0].Y;
                double r  = Math.Abs(el.Points[1].X - cx);
                minX = Math.Min(minX, cx - r); minY = Math.Min(minY, cy - r);
                maxX = Math.Max(maxX, cx + r); maxY = Math.Max(maxY, cy + r);
            }
            else
            {
                foreach (var p in el.Points)
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                }
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static double D(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;

        private static SymbolPoint SP(double x, double y) => new SymbolPoint(x, y);

        private static SymbolElement CloneShell(SymbolElement src) => new SymbolElement
        {
            Type              = src.Type,
            StrokeColor       = src.StrokeColor,
            StrokeThicknessMm = src.StrokeThicknessMm,
            IsFilled          = src.IsFilled,
            FillColor         = src.FillColor,
            IsClosed          = src.IsClosed
        };
    }
}
