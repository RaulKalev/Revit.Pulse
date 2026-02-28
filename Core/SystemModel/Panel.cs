using System;
using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Represents a fire alarm control panel.
    /// A panel is the top-level grouping entity that contains loops.
    /// </summary>
    public class Panel : ISystemEntity
    {
        public string EntityId { get; }
        public string DisplayName { get; set; }
        public string EntityType => "Panel";
        public long? RevitElementId { get; set; }

        /// <summary>
        /// Elevation in Revit internal units (feet) of the level the panel is placed on.
        /// Null if not resolved (e.g. panel has no associated Revit level).
        /// </summary>
        public double? Elevation { get; set; }

        /// <summary>X coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationX { get; set; }

        /// <summary>Y coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationY { get; set; }

        /// <summary>Absolute Z coordinate in Revit internal units (feet). Null if not resolved.</summary>
        public double? LocationZ { get; set; }

        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Properties => _properties;

        /// <summary>Loops assigned to this panel.</summary>
        public List<Loop> Loops { get; } = new List<Loop>();

        public Panel(string entityId, string displayName)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            DisplayName = displayName ?? entityId;
        }

        /// <summary>Set or overwrite a property value.</summary>
        public void SetProperty(string key, string value)
        {
            _properties[key] = value;
        }

        public override string ToString() => $"Panel: {DisplayName}";
    }
}
