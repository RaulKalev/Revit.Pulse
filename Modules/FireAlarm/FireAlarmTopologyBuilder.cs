using Pulse.Core.Graph;
using Pulse.Core.Modules;
using Pulse.Core.SystemModel;

namespace Pulse.Modules.FireAlarm
{
    /// <summary>
    /// Builds the topology graph for the Fire Alarm module.
    /// Produces a hierarchical graph: Panel -> Loop -> Device.
    /// Zones are optional and added when zone data is available.
    /// </summary>
    public class FireAlarmTopologyBuilder : ITopologyBuilder
    {
        public void Build(ModuleData data)
        {
            data.Nodes.Clear();
            data.Edges.Clear();

            // Create panel nodes
            foreach (Panel panel in data.Panels)
            {
                var node = new Node(panel.EntityId, panel.DisplayName, "Panel");
                data.Nodes.Add(node);
            }

            // Create loop nodes and connect to panels
            foreach (Loop loop in data.Loops)
            {
                var node = new Node(loop.EntityId, loop.DisplayName, "Loop");
                node.Properties["DeviceCount"] = loop.Devices.Count.ToString();
                data.Nodes.Add(node);

                if (!string.IsNullOrEmpty(loop.PanelId))
                {
                    data.Edges.Add(new Edge(loop.PanelId, loop.EntityId, "contains"));
                }
            }

            // Create zone nodes (if any)
            foreach (Zone zone in data.Zones)
            {
                var node = new Node(zone.EntityId, zone.DisplayName, "Zone");
                data.Nodes.Add(node);

                // Connect zone devices
                foreach (string deviceId in zone.DeviceIds)
                {
                    data.Edges.Add(new Edge(zone.EntityId, deviceId, "includes"));
                }
            }

            // Create device nodes and connect to loops
            foreach (AddressableDevice device in data.Devices)
            {
                var node = new Node(device.EntityId, device.DisplayName, "Device")
                {
                    RevitElementId = device.RevitElementId,
                };

                // Copy device properties to the node
                foreach (var kvp in device.Properties)
                {
                    node.Properties[kvp.Key] = kvp.Value;
                }

                if (!string.IsNullOrEmpty(device.Address))
                {
                    node.Properties["Address"] = device.Address;
                }

                if (!string.IsNullOrEmpty(device.DeviceType))
                {
                    node.Properties["DeviceType"] = device.DeviceType;
                }

                data.Nodes.Add(node);

                if (!string.IsNullOrEmpty(device.LoopId))
                {
                    data.Edges.Add(new Edge(device.LoopId, device.EntityId, "contains"));
                }
            }
        }
    }
}
