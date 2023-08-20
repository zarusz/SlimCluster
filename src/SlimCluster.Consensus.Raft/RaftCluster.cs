namespace SlimCluster.Consensus.Raft;

using Microsoft.Extensions.Options;

using SlimCluster.Membership;

public class RaftCluster : ICluster
{
    private readonly IClusterMembership _clusterMembership;
    private readonly RaftNode _raftNode;

    public string ClusterId { get; protected set; }

    public IClusterStatus Status { get; protected set; }

    public IReadOnlyCollection<INode> Members => _clusterMembership.Members.Select(x => x.Node).ToList();

    public IReadOnlyCollection<INode> OtherMembers => _clusterMembership.OtherMembers.Select(x => x.Node).ToList();

    public INode? LeaderNode => _raftNode.LeaderNode;

    public INode SelfNode => _clusterMembership.SelfMember.Node;

    public RaftCluster(IOptions<ClusterOptions> clusterOptions, IClusterMembership clusterMembership, RaftNode raftNode)
    {
        _clusterMembership = clusterMembership;
        _raftNode = raftNode;
        ClusterId = clusterOptions.Value.ClusterId;
        // ToDo: Keep on updating the status
        Status = RaftClusterStatus.Initializing;
    }
}

