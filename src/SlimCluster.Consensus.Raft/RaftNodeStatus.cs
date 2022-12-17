namespace SlimCluster.Consensus.Raft;

using SlimCluster;

public class RaftNodeStatus : AbstractStatus<RaftNodeStatus>, INodeStatus
{
    protected RaftNodeStatus(Guid id, string name) : base(id, name)
    {
    }

    public static readonly RaftNodeStatus Unknown = new(new("{8593EAB9-1864-43E3-A700-4622FE3105ED}"), "Unknown");
    public static readonly RaftNodeStatus Leader = new(new("{FF564377-5F12-473E-8FC0-E44BAFE74BA8}"), "Leader");
    public static readonly RaftNodeStatus Follower = new(new("{0F5DB060-5ADB-4973-9EEE-A6B8FD5CEEC8}"), "Follower");
    public static readonly RaftNodeStatus Candidate = new(new("{BC46D901-FDE0-4D54-8E7F-78129627138C}"), "Candidate");
}
