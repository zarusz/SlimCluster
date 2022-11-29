namespace SlimCluster.Consensus.Raft;

public class RaftCluster : ICluster
{
    public string ClusterId { get; protected set; }

    public IReadOnlyCollection<INode> Members { get; protected set; }

    public INode? LeaderNode { get; protected set; }

    public IClusterStatus Status { get; protected set; }

    public RaftCluster(string clusterId)
    {
        ClusterId = clusterId;
        Members = new List<INode>();
        LeaderNode = null;
        Status = RaftClusterStatus.Initializing;
    }
}

