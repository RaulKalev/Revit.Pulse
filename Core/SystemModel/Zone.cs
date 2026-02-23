using System;
using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Represents a logical zone in the fire alarm system.
    /// A zone groups devices for alarm reporting purposes.
    /// Optional in the MVP — topology can be built without zones.
    /// </summary>
    public class Zone : ISystemEntity
    {
        public string EntityId { get; }
        public string DisplayName { get; set; }
        public string EntityType => "Zone";
        public long? RevitElementId { get; set; }

        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Properties => _properties;

        /// <summary>Device entity ids assigned to this zone.</summary>
        public List<string> DeviceIds { get; } = new List<string>();

        public Zone(string entityId, string displayName)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            DisplayName = displayName ?? entityId;
        }

        public void SetProperty(string key, string value)
        {
            _properties[key] = value;
        }

        public override string ToString() => $"Zone: {DisplayName}";
    }
}
