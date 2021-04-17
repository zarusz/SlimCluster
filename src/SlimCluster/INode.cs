namespace SlimCluster
{
    public interface INode
    {
        /// <summary>
        /// Some identifier of the node.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Status of the node as visible to from the cluster.
        /// </summary>
        INodeStatus Status { get; }
    }
}
