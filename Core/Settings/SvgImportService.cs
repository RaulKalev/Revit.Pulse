using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Parses an SVG file and converts supported elements into
    /// <see cref="SymbolElement"/> objects.
    /// Supported SVG elements: line, polyline, polygon, circle, ellipse, rect, path, g.
    /// Path support covers M/m, L/l, H/h, V/v, Z/z commands; curves are linearised.
    /// All coordinates are normalised to fit the target canvas size.
    /// </summary>
    public static class SvgImportService
    {
        private const string DefaultStroke  = "#FFFFFF";
        private const double DefaultThickMm = 0.5;

        // ─── Public API ───────────────────────────────────────────────────────

        public static List<SymbolElement> Import(string filePath,
                                                  double targetWidthMm  = 20.0,
                                                  double targetHeightMm = 20.0)
        {
            if (!File.Exists(filePath)) return new List<SymbolElement>();

            XDocument doc;
            try { doc = XDocument.Load(filePath); }
            catch { return new List<SymbolElement>(); }

            var raw = new List<SymbolElement>();
            ParseGroup(doc.Root, raw);
            return Normalize(raw, targetWidthMm, targetHeightMm);
        }

        // ─── Recursive element parsing ────────────────────────────────────────

        private static void ParseGroup(XElement container, List<SymbolElement> out_)
        {
            if (container == null) return;
            XNamespace ns = container.Name.Namespace;

            foreach (var child in container.Elements())
            {
                string tag = child.Name.LocalName.ToLowerInvariant();
                switch (tag)
                {
                    case "g":        ParseGroup(child, out_);           break;
                    case "line":     TryAdd(ParseLine(child),     out_); break;
                    case "polyline": TryAdd(ParsePolyPoints(child, false), out_); break;
                    case "polygon":  TryAdd(ParsePolyPoints(child, true),  out_); break;
                    case "circle":   TryAdd(ParseCircle(child),   out_); break;
                    case "ellipse":  TryAdd(ParseEllipse(child),  out_); break;
                    case "rect":     TryAdd(ParseRect(child),     out_); break;
                    case "path":     TryAdd(ParsePath(child),     out_); break;
                }
            }
        }

        private static void TryAdd(SymbolElement el, List<SymbolElement> list)
        {
            if (el != null) list.Add(el);
        }

        // ─── Element parsers ──────────────────────────────────────────────────

        private static SymbolElement ParseLine(XElement el)
        {
            double x1 = F(el, "x1"), y1 = F(el, "y1");
            double x2 = F(el, "x2"), y2 = F(el, "y2");
            if (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9) return null;
            return Make(SymbolElementType.Line,
                        new List<SymbolPoint> { SP(x1, y1), SP(x2, y2) }, el);
        }

        private static SymbolElement ParsePolyPoints(XElement el, bool closed)
        {
            var pts = ParsePointsList(el.Attribute("points")?.Value ?? "");
            if (pts.Count < 2) return null;
            var sym = Make(SymbolElementType.Polyline, pts, el);
            if (closed) sym.IsClosed = true;
            if (closed && HasFill(el)) sym.IsFilled = true;
            return sym;
        }

        private static SymbolElement ParseCircle(XElement el)
        {
            double cx = F(el, "cx"), cy = F(el, "cy"), r = F(el, "r");
            if (r < 1e-9) return null;
            var sym = Make(SymbolElementType.Circle,
                           new List<SymbolPoint> { SP(cx, cy), SP(cx + r, cy) }, el);
            if (HasFill(el)) sym.IsFilled = true;
            return sym;
        }

        private static SymbolElement ParseEllipse(XElement el)
        {
            // Approximate as polyline since SymbolElement has Circle (uniform radius only)
            double cx = F(el, "cx"), cy = F(el, "cy");
            double rx = F(el, "rx"), ry = F(el, "ry");
            if (rx < 1e-9 || ry < 1e-9) return null;

            int segs = 48;
            var pts = new List<SymbolPoint>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                double a = 2 * Math.PI * i / segs;
                pts.Add(SP(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }
            var sym = Make(SymbolElementType.Polyline, pts, el);
            sym.IsClosed = true;
            if (HasFill(el)) sym.IsFilled = true;
            return sym;
        }

        private static SymbolElement ParseRect(XElement el)
        {
            double x = F(el, "x"), y = F(el, "y");
            double w = F(el, "width"), h = F(el, "height");
            if (w < 1e-9 || h < 1e-9) return null;
            var sym = Make(SymbolElementType.Rectangle,
                           new List<SymbolPoint> { SP(x, y), SP(x + w, y + h) }, el);
            if (HasFill(el)) sym.IsFilled = true;
            return sym;
        }

        private static SymbolElement ParsePath(XElement el)
        {
            string d = el.Attribute("d")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(d)) return null;

            var pts  = new List<SymbolPoint>();
            bool closed = false;
            double curX = 0, curY = 0;
            double startX = 0, startY = 0;

            // Tokenise the path data
            var tokens = TokenisePath(d);
            int i = 0;
            char cmd = 'M';

            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.IsCommand) { cmd = t.Cmd; i++; continue; }

                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                    {
                        double x = t.Val, y = Next(tokens, ref i);
                        if (char.IsLower(cmd)) { x += curX; y += curY; }
                        curX = startX = x; curY = startY = y;
                        pts.Add(SP(curX, curY));
                        cmd = char.IsLower(cmd) ? 'l' : 'L'; // subsequent coords = lineto
                        break;
                    }
                    case 'L':
                    {
                        double x = t.Val, y = Next(tokens, ref i);
                        if (char.IsLower(cmd)) { x += curX; y += curY; }
                        curX = x; curY = y;
                        pts.Add(SP(curX, curY));
                        break;
                    }
                    case 'H':
                    {
                        double x = char.IsLower(cmd) ? curX + t.Val : t.Val;
                        i++;
                        curX = x;
                        pts.Add(SP(curX, curY));
                        continue;
                    }
                    case 'V':
                    {
                        double y = char.IsLower(cmd) ? curY + t.Val : t.Val;
                        i++;
                        curY = y;
                        pts.Add(SP(curX, curY));
                        continue;
                    }
                    case 'C': case 'S': case 'Q': case 'T':
                    {
                        // Skip control points; consume this curve's parameter count
                        int paramCount = char.ToUpper(cmd) == 'C' ? 6
                                       : char.ToUpper(cmd) == 'S' ? 4
                                       : char.ToUpper(cmd) == 'Q' ? 4
                                       : 2; // T
                        double ex = 0, ey = 0;
                        for (int k = 0; k < paramCount && i < tokens.Count && !tokens[i].IsCommand; k++)
                        {
                            if (k == paramCount - 2) ex = tokens[i].Val;
                            if (k == paramCount - 1) ey = tokens[i].Val;
                            i++;
                        }
                        if (char.IsLower(cmd)) { ex += curX; ey += curY; }
                        curX = ex; curY = ey;
                        pts.Add(SP(curX, curY));
                        continue;
                    }
                    case 'A':
                    {
                        // Arc — consume 7 params, jump to endpoint
                        double ex = 0, ey = 0;
                        for (int k = 0; k < 7 && i < tokens.Count && !tokens[i].IsCommand; k++)
                        {
                            if (k == 5) ex = tokens[i].Val;
                            if (k == 6) ey = tokens[i].Val;
                            i++;
                        }
                        if (char.IsLower(cmd)) { ex += curX; ey += curY; }
                        curX = ex; curY = ey;
                        pts.Add(SP(curX, curY));
                        continue;
                    }
                    case 'Z':
                    {
                        closed = true;
                        curX = startX; curY = startY;
                        i++;
                        continue;
                    }
                    default:
                        i++;
                        continue;
                }
                i++;
            }

            if (pts.Count < 2) return null;
            var sym = Make(SymbolElementType.Polyline, pts, el);
            sym.IsClosed = closed;
            if (closed && HasFill(el)) sym.IsFilled = true;
            return sym;
        }

        // ─── Path tokeniser ───────────────────────────────────────────────────

        private struct PathToken
        {
            public bool   IsCommand;
            public char   Cmd;
            public double Val;
        }

        private static List<PathToken> TokenisePath(string d)
        {
            var result = new List<PathToken>();
            int i = 0;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsLetter(c))
                {
                    result.Add(new PathToken { IsCommand = true, Cmd = c });
                    i++;
                }
                else if (c == '-' || c == '.' || char.IsDigit(c))
                {
                    int start = i;
                    if (d[i] == '-') i++;
                    while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.')) i++;
                    // second decimal — stop
                    if (double.TryParse(d.Substring(start, i - start),
                                        NumberStyles.Float, CultureInfo.InvariantCulture,
                                        out double v))
                        result.Add(new PathToken { IsCommand = false, Val = v });
                }
                else
                {
                    i++; // whitespace, comma, etc.
                }
            }
            return result;
        }

        private static double Next(List<PathToken> tokens, ref int i)
        {
            i++;
            if (i < tokens.Count && !tokens[i].IsCommand) return tokens[i].Val;
            return 0;
        }

        // ─── Attribute helpers ────────────────────────────────────────────────

        private static double F(XElement el, string attr)
        {
            var a = el.Attribute(attr)?.Value ?? "0";
            // Strip units (px, pt, cm, mm, %)
            a = a.TrimEnd('%', ' ');
            foreach (var unit in new[] { "px", "pt", "cm", "mm", "em", "rem" })
                if (a.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                { a = a.Substring(0, a.Length - unit.Length); break; }
            return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        private static bool HasFill(XElement el)
        {
            string fill = el.Attribute("fill")?.Value
                       ?? GetStyleProp(el.Attribute("style")?.Value, "fill")
                       ?? "";
            return !string.IsNullOrWhiteSpace(fill) && fill != "none" && fill != "transparent";
        }

        private static string GetStyleProp(string style, string prop)
        {
            if (string.IsNullOrWhiteSpace(style)) return null;
            foreach (var part in style.Split(';'))
            {
                var kv = part.Trim().Split(':');
                if (kv.Length == 2 && kv[0].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }
            return null;
        }

        private static string GetStrokeColor(XElement el)
        {
            string s = el.Attribute("stroke")?.Value
                    ?? GetStyleProp(el.Attribute("style")?.Value, "stroke")
                    ?? "";
            if (!string.IsNullOrWhiteSpace(s) && s != "none")
            {
                // Convert rgb(r,g,b) to hex
                if (s.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = s.Substring(4).TrimEnd(')').Split(',');
                    if (parts.Length == 3 &&
                        int.TryParse(parts[0].Trim(), out int r) &&
                        int.TryParse(parts[1].Trim(), out int g) &&
                        int.TryParse(parts[2].Trim(), out int b))
                        return $"#{r:X2}{g:X2}{b:X2}";
                }
                if (s.StartsWith("#")) return s.ToUpperInvariant();
            }
            return DefaultStroke;
        }

        private static List<SymbolPoint> ParsePointsList(string pts)
        {
            var result  = new List<SymbolPoint>();
            var numbers = pts.Split(new[] { ' ', ',', '\t', '\n', '\r' },
                                    StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < numbers.Length; i += 2)
            {
                if (double.TryParse(numbers[i],     NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(numbers[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                    result.Add(SP(x, y));
            }
            return result;
        }

        // ─── Factory ─────────────────────────────────────────────────────────

        private static SymbolElement Make(SymbolElementType type, List<SymbolPoint> pts, XElement el)
            => new SymbolElement
            {
                Type              = type,
                Points            = pts,
                StrokeColor       = GetStrokeColor(el),
                StrokeThicknessMm = DefaultThickMm
            };

        // ─── Normalisation ────────────────────────────────────────────────────

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

            const double pad = 0.10;
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
                    newEl.Points = new List<SymbolPoint>
                    {
                        SP(ocx * scale + offX, ocy * scale + offY),
                        SP(ocx * scale + offX + or_ * scale, ocy * scale + offY)
                    };
                }
                else
                {
                    newEl.Points = el.Points
                        .Select(p => SP(p.X * scale + offX, p.Y * scale + offY))
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

        // ─── Tiny helpers ─────────────────────────────────────────────────────

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
