namespace SlimCluster;

public interface ICluster
{
    /// <summary>
    /// Logical name of the cluster.
    /// </summary>
    string ClusterId { get; }

    /// <summary>
    /// Nodes participating in the cluster (changing dynamically).
    /// </summary>
    IReadOnlyCollection<INode> Members { get; }

    /// <summary>
    /// Current elected leader or null if no leader elected yet.
    /// </summary>
    INode? LeaderNode { get; }

    IClusterStatus Status { get; }

    // OnMemberAnnunced
}
