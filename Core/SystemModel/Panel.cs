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
