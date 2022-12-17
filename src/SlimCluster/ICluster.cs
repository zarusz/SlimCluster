namespace SlimCluster;

public interface ICluster
{
    /// <summary>
    /// Logical name of the cluster.
    /// </summary>
    string ClusterId { get; }

    IClusterStatus Status { get; }

    /// <summary>
    /// Nodes participating in the cluster (changing dynamically) including <see cref="SelfNode"/>.
    /// </summary>
    IReadOnlyCollection<INode> Members { get; }

    /// <summary>
    /// Nodes participating in the cluster (changing dynamically) excluding <see cref="SelfNode"/>.
    /// </summary>
    IReadOnlyCollection<INode> OtherMembers { get; }

    /// <summary>
    /// Current elected leader or null if no leader elected yet.
    /// </summary>
    INode? LeaderNode { get; }

    /// <summary>
    /// The node that is the current running process.
    /// </summary>
    INode SelfNode { get; }

    // OnMemberAnnunced
}
