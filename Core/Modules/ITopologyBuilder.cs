namespace Pulse.Core.Modules
{
    /// <summary>
    /// Builds the topology graph (Nodes + Edges) from the entities in ModuleData.
    /// Called after the collector has populated devices, panels, loops, and zones.
    /// </summary>
    public interface ITopologyBuilder
    {
        /// <summary>
        /// Build the topology graph from the entities already present in ModuleData.
        /// Populates ModuleData.Nodes and ModuleData.Edges.
        /// </summary>
        /// <param name="data">ModuleData with populated entity lists.</param>
        void Build(ModuleData data);
    }
}
