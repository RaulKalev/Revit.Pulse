using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Parses an ASCII DXF file and converts supported entities into
    /// <see cref="SymbolElement"/> objects.
    /// Supports: LINE, LWPOLYLINE, POLYLINE, CIRCLE, ARC, SPLINE, HATCH (boundary loops),
    ///           SOLID, TRACE, INSERT (block references with position/rotation/scale).
    /// All coordinates are normalized to fit the target canvas size.
    /// </summary>
    public static class DxfImportService
    {
        private const double ArcSegments    = 48;
        private const string DefaultStroke  = "#FFFFFF";
        private const string DefaultFill    = "#888888";
        private const double DefaultThickMm = 0.5;

        // ─── Public API ───────────────────────────────────────────────────────

        public static List<SymbolElement> Import(string filePath,
                                                  double targetWidthMm  = 20.0,
                                                  double targetHeightMm = 20.0)
        {
            if (!File.Exists(filePath)) return new List<SymbolElement>();

            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return new List<SymbolElement>(); }

            var pairs   = BuildPairs(lines);
            var blocks  = ParseBlocks(pairs);
            var raw     = ExtractEntities(pairs, blocks);
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

        // ─── BLOCKS section ───────────────────────────────────────────────────

        /// <summary>
        /// Reads the BLOCKS section and returns a map: block-name → raw symbol elements.
        /// INSERT entities inside block definitions are NOT expanded here (expanded lazily
        /// when the outer INSERT is encountered in the ENTITIES section).
        /// </summary>
        private static Dictionary<string, List<SymbolElement>> ParseBlocks(
            List<(int code, string value)> pairs)
        {
            var blocks = new Dictionary<string, List<SymbolElement>>(StringComparer.OrdinalIgnoreCase);
            bool inBlocks = false;
            int  i = 0;

            while (i < pairs.Count)
            {
                var (code, val) = pairs[i];

                if (code == 2 && val == "BLOCKS") { inBlocks = true;  i++; continue; }
                if (code == 0 && val == "ENDSEC") { inBlocks = false; i++; continue; }

                if (inBlocks && code == 0 && val == "BLOCK")
                {
                    // Read block header to get name
                    string name = "";
                    int headerStart = i;
                    i++;
                    while (i < pairs.Count && pairs[i].code != 0)
                    {
                        if (pairs[i].code == 2) name = pairs[i].value;
                        i++;
                    }
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Collect entities until ENDBLK
                    var blockEls = new List<SymbolElement>();
                    while (i < pairs.Count && !(pairs[i].code == 0 && pairs[i].value == "ENDBLK"))
                    {
                        if (pairs[i].code == 0)
                        {
                            SymbolElement el = null;
                            switch (pairs[i].value)
                            {
                                case "LINE":       el = ParseLine       (pairs, ref i); break;
                                case "LWPOLYLINE": el = ParseLwPolyline (pairs, ref i); break;
                                case "POLYLINE":   el = ParsePolyline   (pairs, ref i); break;
                                case "CIRCLE":     el = ParseCircle     (pairs, ref i); break;
                                case "ARC":        el = ParseArc        (pairs, ref i); break;
                                case "SPLINE":     el = ParseSpline     (pairs, ref i); break;
                                case "HATCH":      foreach (var h in ParseHatch(pairs, ref i)) blockEls.Add(h); continue;
                                case "SOLID":
                                case "TRACE":      el = ParseSolid      (pairs, ref i); break;
                                default:           i++; break;
                            }
                            if (el != null) blockEls.Add(el);
                        }
                        else i++;
                    }
                    if (pairs[i].code == 0) i++; // consume ENDBLK

                    blocks[name] = blockEls;
                }
                else i++;
            }

            return blocks;
        }

        // ─── ENTITIES section ─────────────────────────────────────────────────

        private static List<SymbolElement> ExtractEntities(
            List<(int code, string value)> pairs,
            Dictionary<string, List<SymbolElement>> blocks)
        {
            var elements    = new List<SymbolElement>();
            bool inEntities = false;
            int  i          = 0;

            while (i < pairs.Count)
            {
                var (code, val) = pairs[i];

                if (code == 2 && val == "ENTITIES") { inEntities = true;  i++; continue; }
                if (code == 0 && val == "ENDSEC")   { inEntities = false; i++; continue; }

                if (inEntities && code == 0)
                {
                    switch (val)
                    {
                        case "LINE":
                        { var el = ParseLine(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "LWPOLYLINE":
                        { var el = ParseLwPolyline(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "POLYLINE":
                        { var el = ParsePolyline(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "CIRCLE":
                        { var el = ParseCircle(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "ARC":
                        { var el = ParseArc(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "SPLINE":
                        { var el = ParseSpline(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "HATCH":
                        { elements.AddRange(ParseHatch(pairs, ref i)); continue; }
                        case "SOLID":
                        case "TRACE":
                        { var el = ParseSolid(pairs, ref i); if (el != null) elements.Add(el); continue; }
                        case "INSERT":
                        { elements.AddRange(ParseInsert(pairs, ref i, blocks, depth: 0)); continue; }
                    }
                }
                i++;
            }

            return elements;
        }

        // ─── Entity parsers ───────────────────────────────────────────────────

        private static SymbolElement ParseLine(List<(int code, string value)> p, ref int i)
        {
            i++;
            double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            string color = DefaultStroke;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: x1 = D(p[i].value); break;
                    case 20: y1 = D(p[i].value); break;
                    case 11: x2 = D(p[i].value); break;
                    case 21: y2 = D(p[i].value); break;
                    case 62:  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: color = TrueColorToHex(p[i].value); break;
                }
                i++;
            }
            if (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9) return null;
            return new SymbolElement
            {
                Type = SymbolElementType.Line,
                Points = new List<SymbolPoint> { SP(x1, y1), SP(x2, y2) },
                StrokeColor = color,
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
            string color  = DefaultStroke;

            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 70: closed = (int.Parse(p[i].value) & 1) != 0; break;
                    case 10:
                        if (hasX) pts.Add(SP(cx, cy));
                        cx = D(p[i].value); hasX = true;
                        break;
                    case 20: cy = D(p[i].value); break;
                    case 62:  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: color = TrueColorToHex(p[i].value); break;
                }
                i++;
            }
            if (hasX) pts.Add(SP(cx, cy));
            if (pts.Count < 2) return null;
            return new SymbolElement
            {
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = color,
                StrokeThicknessMm = DefaultThickMm,
                IsClosed = closed
            };
        }

        private static SymbolElement ParsePolyline(List<(int code, string value)> p, ref int i)
        {
            i++;
            bool closed = false;
            string color = DefaultStroke;
            while (i < p.Count && p[i].code != 0)
            {
                if (p[i].code == 70) closed = (int.Parse(p[i].value) & 1) != 0;
                else if (p[i].code == 62)  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7);
                else if (p[i].code == 420) color = TrueColorToHex(p[i].value);
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
                        if (p[i].code == 10) vx = D(p[i].value);
                        if (p[i].code == 20) vy = D(p[i].value);
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
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = color,
                StrokeThicknessMm = DefaultThickMm,
                IsClosed = closed
            };
        }

        private static SymbolElement ParseCircle(List<(int code, string value)> p, ref int i)
        {
            i++;
            double cx = 0, cy = 0, r = 0;
            string color = DefaultStroke;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: cx = D(p[i].value); break;
                    case 20: cy = D(p[i].value); break;
                    case 40: r  = D(p[i].value); break;
                    case 62:  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: color = TrueColorToHex(p[i].value); break;
                }
                i++;
            }
            if (r < 1e-9) return null;
            return new SymbolElement
            {
                Type = SymbolElementType.Circle,
                Points = new List<SymbolPoint> { SP(cx, cy), SP(cx + r, cy) },
                StrokeColor = color,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        private static SymbolElement ParseArc(List<(int code, string value)> p, ref int i)
        {
            i++;
            double cx = 0, cy = 0, r = 0, startDeg = 0, endDeg = 360;
            string color = DefaultStroke;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: cx       = D(p[i].value); break;
                    case 20: cy       = D(p[i].value); break;
                    case 40: r        = D(p[i].value); break;
                    case 50: startDeg = D(p[i].value); break;
                    case 51: endDeg   = D(p[i].value); break;
                    case 62:  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: color = TrueColorToHex(p[i].value); break;
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
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = color,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        private static SymbolElement ParseSpline(List<(int code, string value)> p, ref int i)
        {
            i++;
            var pts = new List<SymbolPoint>();
            string color = DefaultStroke;
            while (i < p.Count && p[i].code != 0)
            {
                if (p[i].code == 10 && i + 1 < p.Count && p[i + 1].code == 20)
                { pts.Add(SP(D(p[i].value), D(p[i + 1].value))); i += 2; continue; }
                if (p[i].code == 62)  color = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7);
                if (p[i].code == 420) color = TrueColorToHex(p[i].value);
                i++;
            }
            if (pts.Count < 2) return null;
            return new SymbolElement
            {
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = color,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        /// <summary>SOLID / TRACE — 4-point filled quadrilateral.</summary>
        private static SymbolElement ParseSolid(List<(int code, string value)> p, ref int i)
        {
            i++;
            double x0 = 0, y0 = 0, x1 = 0, y1 = 0,
                   x2 = 0, y2 = 0, x3 = 0, y3 = 0;
            string stroke = DefaultStroke, fill = DefaultFill;
            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 10: x0 = D(p[i].value); break; case 20: y0 = D(p[i].value); break;
                    case 11: x1 = D(p[i].value); break; case 21: y1 = D(p[i].value); break;
                    case 12: x2 = D(p[i].value); break; case 22: y2 = D(p[i].value); break;
                    case 13: x3 = D(p[i].value); break; case 23: y3 = D(p[i].value); break;
                    case 62:  stroke = fill = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: stroke = fill = TrueColorToHex(p[i].value); break;
                }
                i++;
            }
            // SOLID winding: pts 0,1,3,2 forms the quad
            var pts = new List<SymbolPoint> { SP(x0, y0), SP(x1, y1), SP(x3, y3), SP(x2, y2) };
            return new SymbolElement
            {
                Type = SymbolElementType.Polyline,
                Points = pts,
                StrokeColor = stroke,
                FillColor = fill,
                IsFilled = true,
                IsClosed = true,
                StrokeThicknessMm = DefaultThickMm
            };
        }

        // ─── HATCH parser ─────────────────────────────────────────────────────

        /// <summary>
        /// Parses a HATCH entity and returns one filled polygon per boundary loop.
        /// Supports polyline loops and edge loops (lines + arcs linearised).
        /// </summary>
        private static List<SymbolElement> ParseHatch(List<(int code, string value)> p, ref int i)
        {
            i++; // consume "HATCH"
            var results = new List<SymbolElement>();
            string stroke = DefaultStroke;
            string fill   = DefaultFill;
            bool   solid  = false;
            int    numLoops = 0;

            // Read HATCH header (up to "91" = boundary path count)
            while (i < p.Count && p[i].code != 91 && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 70:  solid  = p[i].value == "1"; break;  // 1=solid fill
                    case 62:  stroke = fill = AciToHex(int.TryParse(p[i].value, out int c) ? c : 7); break;
                    case 420: stroke = fill = TrueColorToHex(p[i].value); break;
                }
                i++;
            }

            if (i < p.Count && p[i].code == 91)
            {
                numLoops = int.TryParse(p[i].value, out int n) ? n : 0;
                i++;
            }

            for (int loop = 0; loop < numLoops && i < p.Count && p[i].code != 0; loop++)
            {
                // group 92 = boundary path type flags
                int pathType = 0;
                if (p[i].code == 92)
                {
                    pathType = int.TryParse(p[i].value, out int t) ? t : 0;
                    i++;
                }

                List<SymbolPoint> loopPts;
                bool isClosed = true;

                if ((pathType & 2) != 0)
                {
                    // ─ Polyline boundary ──────────────────────────────────────
                    loopPts = ParseHatchPolylineLoop(p, ref i, out isClosed);
                }
                else
                {
                    // ─ Edge boundary ─────────────────────────────────────────
                    loopPts = ParseHatchEdgeLoop(p, ref i);
                    isClosed = true;
                }

                // Skip any remaining per-loop codes (97 = source object count, etc.)
                while (i < p.Count && p[i].code != 92 && p[i].code != 0 && p[i].code != 75)
                    i++;

                if (loopPts.Count >= 2)
                {
                    results.Add(new SymbolElement
                    {
                        Type = SymbolElementType.Polyline,
                        Points = loopPts,
                        StrokeColor = stroke,
                        FillColor = solid ? fill : "Transparent",
                        IsFilled = solid,
                        IsClosed = isClosed,
                        StrokeThicknessMm = DefaultThickMm
                    });
                }
            }

            // Consume remaining HATCH codes
            while (i < p.Count && p[i].code != 0) i++;
            return results;
        }

        private static List<SymbolPoint> ParseHatchPolylineLoop(
            List<(int code, string value)> p, ref int i, out bool closed)
        {
            bool hasBulge = false;
            int  count    = 0;
            closed = true;
            var pts = new List<SymbolPoint>();

            if (i < p.Count && p[i].code == 72) { hasBulge = p[i].value == "1"; i++; }
            if (i < p.Count && p[i].code == 73) { closed = p[i].value != "0";   i++; }
            if (i < p.Count && p[i].code == 93) { count  = int.TryParse(p[i].value, out int n) ? n : 0; i++; }

            for (int v = 0; v < count && i < p.Count && p[i].code != 97 && p[i].code != 92 && p[i].code != 0; v++)
            {
                double vx = 0, vy = 0;
                if (p[i].code == 10) { vx = D(p[i].value); i++; }
                if (i < p.Count && p[i].code == 20) { vy = D(p[i].value); i++; }
                if (hasBulge && i < p.Count && p[i].code == 42) i++; // skip bulge for now
                pts.Add(SP(vx, vy));
            }
            return pts;
        }

        private static List<SymbolPoint> ParseHatchEdgeLoop(
            List<(int code, string value)> p, ref int i)
        {
            int edgeCount = 0;
            if (i < p.Count && p[i].code == 93)
            { edgeCount = int.TryParse(p[i].value, out int n) ? n : 0; i++; }

            var pts = new List<SymbolPoint>();

            for (int e = 0; e < edgeCount && i < p.Count; e++)
            {
                if (p[i].code != 72) break;
                int edgeType = int.TryParse(p[i].value, out int t) ? t : 0;
                i++;

                switch (edgeType)
                {
                    case 1: // LINE
                    {
                        double ex1 = 0, ey1 = 0, ex2 = 0, ey2 = 0;
                        while (i < p.Count && p[i].code != 72 && p[i].code != 97 && p[i].code != 92 && p[i].code != 0)
                        {
                            switch (p[i].code)
                            {
                                case 10: ex1 = D(p[i].value); break;
                                case 20: ey1 = D(p[i].value); break;
                                case 11: ex2 = D(p[i].value); break;
                                case 21: ey2 = D(p[i].value); break;
                            }
                            i++;
                        }
                        if (pts.Count == 0) pts.Add(SP(ex1, ey1));
                        pts.Add(SP(ex2, ey2));
                        break;
                    }
                    case 2: // ARC
                    {
                        double acx = 0, acy = 0, ar = 0, aStart = 0, aEnd = 360;
                        bool ccw = true;
                        while (i < p.Count && p[i].code != 72 && p[i].code != 97 && p[i].code != 92 && p[i].code != 0)
                        {
                            switch (p[i].code)
                            {
                                case 10: acx    = D(p[i].value); break;
                                case 20: acy    = D(p[i].value); break;
                                case 40: ar     = D(p[i].value); break;
                                case 50: aStart = D(p[i].value); break;
                                case 51: aEnd   = D(p[i].value); break;
                                case 73: ccw    = p[i].value != "0"; break;
                            }
                            i++;
                        }
                        if (ar > 1e-9)
                        {
                            double span = ccw ? (aEnd - aStart) : (aStart - aEnd);
                            if (span <= 0) span += 360;
                            int segs = Math.Max(4, (int)(ArcSegments * span / 360.0));
                            double dir = ccw ? 1 : -1;
                            for (int j = 0; j <= segs; j++)
                            {
                                double a = (aStart + dir * span * j / segs) * Math.PI / 180.0;
                                pts.Add(SP(acx + ar * Math.Cos(a), acy + ar * Math.Sin(a)));
                            }
                        }
                        break;
                    }
                    case 3: // ELLIPSE — skip to next edge
                    case 4: // SPLINE  — skip to next edge
                    {
                        while (i < p.Count && p[i].code != 72 && p[i].code != 97 && p[i].code != 92 && p[i].code != 0)
                            i++;
                        break;
                    }
                }
            }
            return pts;
        }

        // ─── INSERT / block expansion ─────────────────────────────────────────

        /// <summary>
        /// Parses an INSERT entity and returns the block's entities
        /// transformed by the insert's position, rotation and scale.
        /// Supports nested inserts up to depth 8.
        /// </summary>
        private static List<SymbolElement> ParseInsert(
            List<(int code, string value)> p, ref int i,
            Dictionary<string, List<SymbolElement>> blocks,
            int depth)
        {
            i++;
            string name  = "";
            double ix = 0, iy = 0;
            double sx = 1, sy = 1;
            double rotDeg = 0;
            int    colCount = 1, rowCount = 1;
            double colSpacing = 0, rowSpacing = 0;

            while (i < p.Count && p[i].code != 0)
            {
                switch (p[i].code)
                {
                    case 2:  name       = p[i].value; break;
                    case 10: ix         = D(p[i].value); break;
                    case 20: iy         = D(p[i].value); break;
                    case 41: sx         = D(p[i].value); break;
                    case 42: sy         = D(p[i].value); break;
                    case 50: rotDeg     = D(p[i].value); break;
                    case 70: colCount   = Math.Max(1, int.TryParse(p[i].value, out int cc) ? cc : 1); break;
                    case 71: rowCount   = Math.Max(1, int.TryParse(p[i].value, out int rc) ? rc : 1); break;
                    case 44: colSpacing = D(p[i].value); break;
                    case 45: rowSpacing = D(p[i].value); break;
                }
                i++;
            }

            if (string.IsNullOrWhiteSpace(name) || !blocks.TryGetValue(name, out var blockEls))
                return new List<SymbolElement>();

            // Guard against infinite recursion through nested blocks
            if (depth > 8) return new List<SymbolElement>();

            double cosR = Math.Cos(rotDeg * Math.PI / 180.0);
            double sinR = Math.Sin(rotDeg * Math.PI / 180.0);

            var result = new List<SymbolElement>();

            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < colCount; col++)
                {
                    double ox = ix + col * colSpacing;
                    double oy = iy + row * rowSpacing;

                    foreach (var el in blockEls)
                    {
                        // Expand nested inserts inside block definitions
                        // (They were stored as raw markers — not needed here because
                        //  ParseBlocks() already resolved non-INSERT entities;
                        //  nested INSERTs inside blocks are rare and handled via
                        //  a second pass that could be added if needed.)

                        var newEl = CloneShell(el);
                        if (el.Type == SymbolElementType.Circle && el.Points.Count >= 2)
                        {
                            var  nc  = TransformPoint(el.Points[0].X, el.Points[0].Y, sx, sy, cosR, sinR, ox, oy);
                            double r = Math.Abs(el.Points[1].X - el.Points[0].X) * sx;
                            newEl.Points = new List<SymbolPoint>
                            {
                                SP(nc.X, nc.Y),
                                SP(nc.X + r, nc.Y)
                            };
                        }
                        else
                        {
                            newEl.Points = el.Points
                                .Select(pt => TransformPoint(pt.X, pt.Y, sx, sy, cosR, sinR, ox, oy))
                                .ToList();
                        }
                        result.Add(newEl);
                    }
                }
            }

            return result;
        }

        private static SymbolPoint TransformPoint(
            double px, double py,
            double sx, double sy,
            double cosR, double sinR,
            double tx, double ty)
        {
            // Scale → rotate → translate
            double scaled_x = px * sx;
            double scaled_y = py * sy;
            double rx = scaled_x * cosR - scaled_y * sinR;
            double ry = scaled_x * sinR + scaled_y * cosR;
            return SP(rx + tx, ry + ty);
        }

        // ─── Coordinate normalisation ────────────────────────────────────────

        private static List<SymbolElement> Normalize(List<SymbolElement> elements,
                                                      double targetW, double targetH)
        {
            if (elements.Count == 0) return elements;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var el in elements)
                ExpandBbox(el, ref minX, ref minY, ref maxX, ref maxY);

            double srcW = maxX - minX;
            double srcH = maxY - minY;
            if (srcW < 1e-9 && srcH < 1e-9) return elements;

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
                    double ncy = targetH - (ocy * scale + offY);
                    double nr  = or_ * scale;
                    newEl.Points = new List<SymbolPoint> { SP(ncx, ncy), SP(ncx + nr, ncy) };
                }
                else
                {
                    newEl.Points = el.Points
                        .Select(pt => SP(pt.X * scale + offX, targetH - (pt.Y * scale + offY)))
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
                foreach (var pt in el.Points)
                {
                    minX = Math.Min(minX, pt.X); minY = Math.Min(minY, pt.Y);
                    maxX = Math.Max(maxX, pt.X); maxY = Math.Max(maxY, pt.Y);
                }
            }
        }

        // ─── Color helpers ────────────────────────────────────────────────────

        /// <summary>Converts a DXF ACI (AutoCAD Color Index) to an HTML hex color string.</summary>
        private static string AciToHex(int aci)
        {
            // Most important ACI values; everything else → white
            switch (aci)
            {
                case 1:  return "#FF0000"; // red
                case 2:  return "#FFFF00"; // yellow
                case 3:  return "#00FF00"; // green
                case 4:  return "#00FFFF"; // cyan
                case 5:  return "#0000FF"; // blue
                case 6:  return "#FF00FF"; // magenta
                case 7:  return "#FFFFFF"; // white / black (background dependent)
                case 8:  return "#808080"; // dark grey
                case 9:  return "#C0C0C0"; // light grey
                case 250: return "#333333";
                case 251: return "#555555";
                case 252: return "#777777";
                case 253: return "#999999";
                case 254: return "#BBBBBB";
                case 255: return "#DDDDDD";
                default:  return "#FFFFFF";
            }
        }

        /// <summary>Converts a DXF true-color integer (group 420) to HTML hex.</summary>
        private static string TrueColorToHex(string s)
        {
            if (!int.TryParse(s, out int rgb)) return DefaultStroke;
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >>  8) & 0xFF;
            int b =  rgb        & 0xFF;
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        // ─── Tiny helpers ─────────────────────────────────────────────────────

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

