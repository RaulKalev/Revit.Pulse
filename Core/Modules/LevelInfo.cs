namespace Pulse.Core.Modules
{
    /// <summary>
    /// Lightweight representation of a Revit Level element.
    /// Elevation is stored in feet (Revit internal units) and exposed
    /// in metres as a convenience property.
    /// </summary>
    public class LevelInfo
    {
        /// <summary>Level name, e.g. "Ground Floor", "Level 3".</summary>
        public string Name { get; set; }

        /// <summary>Elevation in Revit internal units (feet).</summary>
        public double Elevation { get; set; }

        /// <summary>Elevation converted to metres (1 ft = 0.3048 m).</summary>
        public double ElevationMeters => Elevation * 0.3048;
    }
}
