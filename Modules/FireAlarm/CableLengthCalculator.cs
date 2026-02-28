using System;
using System.Collections.Generic;
using System.Linq;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Calculates estimated cable (wire) length for fire alarm loops.
    /// 
    /// The cable is routed as an invisible wire starting at the parent panel,
    /// visiting each device in address order, then returning to the panel.
    /// 
    /// Routing uses Manhattan (orthogonal) segments — no diagonal runs —
    /// plus vertical rises/drops between each pair of points.  This matches
    /// real-world cable tray / conduit routing where cables run along walls
    /// at right angles.
    /// 
    /// The design allows a future <see cref="IRoutingStrategy"/> abstraction
    /// for wall-aware or obstacle-aware pathfinding.
    /// </summary>
    public static class CableLengthCalculator
    {
        /// <summary>
        /// Result of a single loop cable-length calculation.
        /// </summary>
        public sealed class LoopCableResult
        {
            /// <summary>Total cable length in metres.</summary>
            public double TotalLengthMetres { get; set; }

            /// <summary>Number of devices that had valid coordinates and were included in routing.</summary>
            public int RoutedDeviceCount { get; set; }

            /// <summary>Number of devices that lacked coordinates and were skipped.</summary>
            public int SkippedDeviceCount { get; set; }

            /// <summary>True when the panel origin was available and used.</summary>
            public bool PanelOriginAvailable { get; set; }
        }

        /// <summary>
        /// Lightweight 3-D point used for routing.
        /// Coordinates are in Revit internal units (feet).
        /// </summary>
        private readonly struct Point3D
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public Point3D(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        private const double FeetToMetres = 0.3048;

        /// <summary>
        /// Calculate the cable length for a single loop.
        /// </summary>
        /// <param name="loop">The loop whose devices to route.</param>
        /// <param name="panel">The parent panel (start/end point of the cable). May be null.</param>
        /// <returns>A <see cref="LoopCableResult"/> with total length and device statistics.</returns>
        public static LoopCableResult Calculate(Loop loop, Panel panel)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));

            var result = new LoopCableResult();

            // Resolve panel origin (start & return point).
            Point3D? panelOrigin = ResolvePoint(panel?.LocationX, panel?.LocationY, panel?.LocationZ);
            result.PanelOriginAvailable = panelOrigin.HasValue;

            // Sort devices by address (numeric then lexicographic fallback).
            var ordered = loop.Devices
                .OrderBy(d => ParseAddressSortKey(d.Address))
                .ThenBy(d => d.Address, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Build the routed waypoint list: panel → device₁ → device₂ → … → panel.
            var waypoints = new List<Point3D>();

            if (panelOrigin.HasValue)
                waypoints.Add(panelOrigin.Value);

            int skipped = 0;
            foreach (var device in ordered)
            {
                Point3D? pt = ResolvePoint(device.LocationX, device.LocationY, device.LocationZ);
                if (pt.HasValue)
                {
                    waypoints.Add(pt.Value);
                }
                else
                {
                    skipped++;
                }
            }

            // Return leg back to panel.
            if (panelOrigin.HasValue && waypoints.Count > 1)
                waypoints.Add(panelOrigin.Value);

            result.RoutedDeviceCount = waypoints.Count - (panelOrigin.HasValue ? 2 : 0); // exclude panel start/end
            result.SkippedDeviceCount = skipped;

            // Sum Manhattan distances between consecutive waypoints.
            double totalFeet = 0;
            for (int i = 1; i < waypoints.Count; i++)
            {
                totalFeet += ManhattanDistance3D(waypoints[i - 1], waypoints[i]);
            }

            result.TotalLengthMetres = totalFeet * FeetToMetres;
            return result;
        }

        /// <summary>
        /// Calculate cable lengths for all loops under a given panel.
        /// </summary>
        public static Dictionary<string, LoopCableResult> CalculateAll(IReadOnlyList<Panel> panels)
        {
            var results = new Dictionary<string, LoopCableResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var panel in panels)
            {
                foreach (var loop in panel.Loops)
                {
                    results[loop.EntityId] = Calculate(loop, panel);
                }
            }

            return results;
        }

        /// <summary>
        /// Manhattan distance in 3-D: |Δx| + |Δy| + |Δz|.
        /// Cables run orthogonally (horizontal X, then horizontal Y, then vertical Z).
        /// </summary>
        private static double ManhattanDistance3D(Point3D a, Point3D b)
        {
            return Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) + Math.Abs(b.Z - a.Z);
        }

        /// <summary>
        /// Resolve nullable coordinates into a <see cref="Point3D"/>.
        /// Returns null when any coordinate is missing.
        /// </summary>
        private static Point3D? ResolvePoint(double? x, double? y, double? z)
        {
            if (x.HasValue && y.HasValue && z.HasValue)
                return new Point3D(x.Value, y.Value, z.Value);
            return null;
        }

        /// <summary>
        /// Extract a numeric sort key from a device address string.
        /// Handles formats like "1", "001", "1.2", or prefixed "A-5".
        /// Falls back to <see cref="int.MaxValue"/> for non-numeric addresses.
        /// </summary>
        private static int ParseAddressSortKey(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return int.MaxValue;

            // Try parsing as a plain integer first (handles "1", "42", "001").
            if (int.TryParse(address, out int intVal))
                return intVal;

            // Try extracting trailing digits (handles "A-5", "L1-12").
            for (int i = address.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(address[i]))
                {
                    string tail = address.Substring(i + 1);
                    if (tail.Length > 0 && int.TryParse(tail, out int tailVal))
                        return tailVal;
                    break;
                }
            }

            return int.MaxValue;
        }
    }
}
