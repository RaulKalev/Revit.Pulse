using System;
using System.Collections.Generic;

namespace Pulse.Core.SystemModel
{
    /// <summary>
    /// Represents a signalling loop within a panel.
    /// A loop contains addressable devices.
    /// </summary>
    public class Loop : ISystemEntity
    {
        public string EntityId { get; }
        public string DisplayName { get; set; }
        public string EntityType => "Loop";
        public long? RevitElementId { get; set; }

        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Properties => _properties;

        /// <summary>Devices assigned to this loop.</summary>
        public List<AddressableDevice> Devices { get; } = new List<AddressableDevice>();

        /// <summary>Optional parent panel identifier.</summary>
        public string PanelId { get; set; }

        public Loop(string entityId, string displayName)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            DisplayName = displayName ?? entityId;
        }

        public void SetProperty(string key, string value)
        {
            _properties[key] = value;
        }

        public override string ToString() => $"Loop: {DisplayName}";
    }
}
