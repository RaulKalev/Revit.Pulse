using System;
using System.Collections.Generic;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Represents a one-way NAC (Notification Appliance Circuit) branch attached to
    /// an Output Module on a loop.  Unlike a loop, a SubCircuit is not bi-directional
    /// and is not closed — it is a single cable run driving sounder/NAC devices.
    ///
    /// SubCircuits are persisted as a JSON blob inside
    /// <see cref="Pulse.Core.Settings.TopologyAssignmentsStore.SubCircuitsJson"/>
    /// and projected into the topology graph as child nodes of their host element.
    /// </summary>
    public class SubCircuit
    {
        /// <summary>Stable GUID-based identifier for this SubCircuit.</summary>
        public string Id { get; set; }

        /// <summary>
        /// Revit ElementId of the host Output Module element.
        /// Stored as <see langword="int"/> so it survives JSON round-trips without precision loss.
        /// </summary>
        public int HostElementId { get; set; }

        /// <summary>User-visible name (e.g. "NAC-01").</summary>
        public string Name { get; set; }

        /// <summary>
        /// Revit ElementIds of the sounder/NAC devices assigned to this SubCircuit.
        /// Order is not semantically significant.
        /// </summary>
        public List<int> DeviceElementIds { get; set; } = new List<int>();

        /// <summary>
        /// Optional wire-type key (matches a wire name in DeviceConfigStore.Wires).
        /// Null/empty means unassigned.
        /// </summary>
        public string WireTypeKey { get; set; }

        /// <summary>
        /// Maximum allowable voltage-drop expressed as a percentage of the nominal supply voltage.
        /// Default 16.7 % (≈ 4 V on a 24 V NAC circuit — the typical EN 54-4 limit).
        /// </summary>
        public double VDropLimitPct { get; set; } = 16.7;

        /// <summary>
        /// Assumed cable conductor temperature in °C for resistance derating.
        /// Copper resistivity rises 0.393 %/°C above 20 °C (BS 7671 Table 4C1 coefficient).
        /// Default 20 °C (standard reference temperature).
        /// </summary>
        public double CableTemperatureDegC { get; set; } = 20.0;

        /// <summary>
        /// End-of-line resistor value in ohms used for supervisory current calculation.
        /// When non-zero the supervisory current V_nom / R_eol is added to normal-mode load.
        /// Default 0 (no EOL resistor / unknown).
        /// </summary>
        public double EolResistorOhms { get; set; } = 0.0;

        /// <summary>
        /// Minimum acceptable end-of-circuit device voltage in volts.
        /// The V-drop check warns when <c>NominalVoltage - VDrop &lt; MinDeviceVoltageV</c>.
        /// Default 16 V — typical minimum for UL 864 / EN 54-4 NAC appliances.
        /// </summary>
        public double MinDeviceVoltageV { get; set; } = 16.0;

        public SubCircuit() { }

        public SubCircuit(string id, int hostElementId, string name)
        {
            Id            = id            ?? throw new ArgumentNullException(nameof(id));
            HostElementId = hostElementId;
            Name          = name          ?? throw new ArgumentNullException(nameof(name));
        }

        public override string ToString() => $"SubCircuit: {Name} (host={HostElementId})";
    }
}
