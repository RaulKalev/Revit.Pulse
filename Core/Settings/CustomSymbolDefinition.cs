using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Pulse.Core.Settings
{
    /// <summary>
    /// Shape primitives the user can draw in the symbol designer.
    /// </summary>
    public enum SymbolElementType
    {
        Line,       // Points[0]=start  Points[1]=end
        Polyline,   // Points[0..n] vertices; IsClosed=true → filled polygon
        Circle,     // Points[0]=centre Points[1]=edge point; radius=distance
        Rectangle   // Points[0]=top-left  Points[1]=bottom-right
    }

    /// <summary>
    /// A 2-D coordinate in millimetres, relative to the symbol viewbox origin (top-left = 0,0).
    /// </summary>
    public class SymbolPoint
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }

        public SymbolPoint() { }
        public SymbolPoint(double x, double y) { X = x; Y = y; }

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    /// <summary>
    /// One drawn primitive inside a custom symbol definition.
    /// All coordinates are in millimetres.
    /// </summary>
    public class SymbolElement
    {
        [JsonProperty("type")]
        public SymbolElementType Type { get; set; }

        /// <summary>Defining points (see <see cref="SymbolElementType"/> docs).</summary>
        [JsonProperty("points")]
        public List<SymbolPoint> Points { get; set; } = new List<SymbolPoint>();

        /// <summary>HTML hex colour for the stroke, e.g. "#FF0000". Null = no stroke.</summary>
        [JsonProperty("strokeColor")]
        public string StrokeColor { get; set; } = "#FF0000";

        /// <summary>Stroke width in millimetres.</summary>
        [JsonProperty("strokeThickness")]
        public double StrokeThicknessMm { get; set; } = 0.35;

        /// <summary>Whether the shape interior should be filled.</summary>
        [JsonProperty("isFilled")]
        public bool IsFilled { get; set; }

        /// <summary>Fill colour when <see cref="IsFilled"/> is true.</summary>
        [JsonProperty("fillColor")]
        public string FillColor { get; set; } = "#FF0000";

        /// <summary>For Polyline only: close the last vertex back to the first.</summary>
        [JsonProperty("isClosed")]
        public bool IsClosed { get; set; }

        /// <summary>Returns a deep copy of this element.</summary>
        public SymbolElement Clone() => new SymbolElement
        {
            Type              = Type,
            Points            = new List<SymbolPoint>(Points.Select(p => new SymbolPoint(p.X, p.Y))),
            StrokeColor       = StrokeColor,
            StrokeThicknessMm = StrokeThicknessMm,
            IsFilled          = IsFilled,
            FillColor         = FillColor,
            IsClosed          = IsClosed
        };
    }

    /// <summary>
    /// A named vector symbol that the user designed in the symbol editor.
    /// Stored as a list inside <c>%APPDATA%\Pulse\custom-symbols.json</c>.
    /// </summary>
    public class CustomSymbolDefinition
    {
        /// <summary>Stable identifier used as lookup key (same as <see cref="Name"/> by default).</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Display name the user typed, used in the symbol-mapping ComboBox.</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "New Symbol";

        /// <summary>Viewbox width in millimetres (default 20 mm).</summary>
        [JsonProperty("viewboxWidth")]
        public double ViewboxWidthMm { get; set; } = 20.0;

        /// <summary>Viewbox height in millimetres (default 20 mm).</summary>
        [JsonProperty("viewboxHeight")]
        public double ViewboxHeightMm { get; set; } = 20.0;

        /// <summary>
        /// X coordinate of the snap / attach-point origin in mm, relative to the viewbox top-left.
        /// When a symbol is placed on the diagram canvas this point is aligned to the device centre.
        /// </summary>
        [JsonProperty("snapOriginX")]
        public double SnapOriginXMm { get; set; } = 0.0;

        /// <summary>
        /// Y coordinate of the snap / attach-point origin in mm, relative to the viewbox top-left.
        /// When a symbol is placed on the diagram canvas this point is aligned to the device centre.
        /// </summary>
        [JsonProperty("snapOriginY")]
        public double SnapOriginYMm { get; set; } = 0.0;

        /// <summary>All drawn primitives that make up the symbol.</summary>
        [JsonProperty("elements")]
        public List<SymbolElement> Elements { get; set; } = new List<SymbolElement>();
    }
}
