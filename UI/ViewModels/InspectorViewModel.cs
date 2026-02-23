using System.Collections.ObjectModel;
using System.Linq;
using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.Rules;

namespace Pulse.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the inspector panel.
    /// Shows details of the currently selected topology node.
    /// </summary>
    public class InspectorViewModel : ViewModelBase
    {
        private string _title;
        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        private string _entityType;
        public string EntityType
        {
            get => _entityType;
            set => SetField(ref _entityType, value);
        }

        private string _entityId;
        public string EntityId
        {
            get => _entityId;
            set => SetField(ref _entityId, value);
        }

        private long? _revitElementId;
        public long? RevitElementId
        {
            get => _revitElementId;
            set => SetField(ref _revitElementId, value);
        }

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set => SetField(ref _warningCount, value);
        }

        private int _childDeviceCount;
        public int ChildDeviceCount
        {
            get => _childDeviceCount;
            set => SetField(ref _childDeviceCount, value);
        }

        private bool _hasSelection;
        public bool HasSelection
        {
            get => _hasSelection;
            set => SetField(ref _hasSelection, value);
        }

        /// <summary>Key-value properties of the selected entity.</summary>
        public ObservableCollection<PropertyItem> Properties { get; } = new ObservableCollection<PropertyItem>();

        /// <summary>Warnings associated with the selected entity.</summary>
        public ObservableCollection<WarningItem> Warnings { get; } = new ObservableCollection<WarningItem>();

        /// <summary>
        /// Load the inspector with data from the selected node.
        /// </summary>
        public void LoadNode(Node node, ModuleData data)
        {
            if (node == null)
            {
                Clear();
                return;
            }

            HasSelection = true;
            Title = node.Label;
            EntityType = node.NodeType;
            EntityId = node.Id;
            RevitElementId = node.RevitElementId;

            // Load properties
            Properties.Clear();
            foreach (var kvp in node.Properties)
            {
                Properties.Add(new PropertyItem { Key = kvp.Key, Value = kvp.Value });
            }

            // Load warnings for this entity
            Warnings.Clear();
            if (data != null)
            {
                var entityWarnings = data.RuleResults
                    .Where(r => r.EntityId == node.Id)
                    .ToList();

                WarningCount = entityWarnings.Count(r => r.Severity >= Severity.Warning);

                foreach (var result in entityWarnings)
                {
                    Warnings.Add(new WarningItem
                    {
                        Severity = result.Severity,
                        RuleName = result.RuleName,
                        Message = result.Message
                    });
                }

                // Count child devices for panels and loops
                if (node.NodeType == "Panel")
                {
                    var panel = data.Panels.Find(p => p.EntityId == node.Id);
                    ChildDeviceCount = panel?.Loops.Sum(l => l.Devices.Count) ?? 0;
                }
                else if (node.NodeType == "Loop")
                {
                    var loop = data.Loops.Find(l => l.EntityId == node.Id);
                    ChildDeviceCount = loop?.Devices.Count ?? 0;
                }
                else
                {
                    ChildDeviceCount = 0;
                }
            }
        }

        /// <summary>Clear the inspector panel.</summary>
        public void Clear()
        {
            HasSelection = false;
            Title = null;
            EntityType = null;
            EntityId = null;
            RevitElementId = null;
            WarningCount = 0;
            ChildDeviceCount = 0;
            Properties.Clear();
            Warnings.Clear();
        }
    }

    /// <summary>Represents a single key-value property in the inspector.</summary>
    public class PropertyItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Represents a single warning in the inspector.</summary>
    public class WarningItem
    {
        public Severity Severity { get; set; }
        public string RuleName { get; set; }
        public string Message { get; set; }

        public string SeverityLabel => Severity.ToString().ToUpperInvariant();
    }
}
