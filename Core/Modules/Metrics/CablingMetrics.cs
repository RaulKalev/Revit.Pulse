using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse.Core.Modules.Metrics
{
    /// <summary>
    /// Cabling data for a single loop.
    /// </summary>
    public sealed class LoopCablingInfo
    {
        public string LoopName     { get; set; }
        public double LengthMetres { get; set; }
        public int    DeviceCount  { get; set; }

        /// <summary>Metres of cable per device (0 when device count is zero).</summary>
        public double MetresPerDevice => DeviceCount > 0 ? Math.Round(LengthMetres / DeviceCount, 1) : 0;

        /// <summary>Formatted "245.3 m" display string.</summary>
        public string LengthDisplay => $"{LengthMetres:F1} m";
    }

    /// <summary>
    /// Aggregated cabling metrics for the selected Panel or Loop.
    /// </summary>
    public sealed class CablingMetrics
    {
        public List<LoopCablingInfo> LoopInfos { get; set; } = new List<LoopCablingInfo>();

        public double TotalLengthMetres =>
            LoopInfos.Count > 0 ? LoopInfos.Sum(l => l.LengthMetres) : 0;

        public string TotalLengthDisplay => $"{TotalLengthMetres:F1} m";

        public string LongestLoopName =>
            LoopInfos.Count > 0
                ? LoopInfos.OrderByDescending(l => l.LengthMetres).First().LoopName
                : "—";

        public double LongestLoopLength =>
            LoopInfos.Count > 0 ? LoopInfos.Max(l => l.LengthMetres) : 0;

        public string LongestLoopDisplay => $"{LongestLoopLength:F1} m";
    }
}
