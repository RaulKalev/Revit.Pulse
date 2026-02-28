using System;
using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Represents a single addressable device on a loop.
    /// This is the leaf-level entity in the system topology.
    /// </summary>
    public class AddressableDevice : ISystemEntity
    {
        public string EntityId { get; }
        public string DisplayName { get; set; }
        public string EntityType => "Device";
        public long? RevitElementId { get; set; }

        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Properties => _properties;

        /// <summary>Address of the device within its loop.</summary>
        public string Address { get; set; }

        /// <summary>Device type (e.g., "Smoke Detector", "Manual Call Point").</summary>
        public string DeviceType { get; set; }

        /// <summary>Current draw in mA (if available).</summary>
        public double? CurrentDraw { get; set; }

        /// <summary>Elevation offset from level in Revit internal units (feet).</summary>
        public double? Elevation { get; set; }

        /// <summary>X coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationX { get; set; }

        /// <summary>Y coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationY { get; set; }

        /// <summary>Absolute Z coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationZ { get; set; }

        /// <summary>Name of the Revit level the device is hosted on.</summary>
        public string LevelName { get; set; }

        /// <summary>Parent loop identifier.</summary>
        public string LoopId { get; set; }

        /// <summary>Parent panel identifier.</summary>
        public string PanelId { get; set; }

        public AddressableDevice(string entityId, string displayName)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            DisplayName = displayName ?? entityId;
        }

        public void SetProperty(string key, string value)
        {
            _properties[key] = value;
        }

        public override string ToString() => $"Device: {DisplayName} (Addr: {Address})";
    }
}
