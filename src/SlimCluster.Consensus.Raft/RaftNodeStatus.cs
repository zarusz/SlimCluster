namespace SlimCluster.Consensus.Raft;

using SlimCluster;

public class RaftNodeStatus : AbstractStatus, INodeStatus
{
    protected RaftNodeStatus(Guid id, string name) : base(id, name)
    {
    }

    public static readonly RaftNodeStatus Leader = new (new Guid("{FF564377-5F12-473E-8FC0-E44BAFE74BA8}"), "Leader");
    public static readonly RaftNodeStatus Follower = new (new Guid("{0F5DB060-5ADB-4973-9EEE-A6B8FD5CEEC8}"), "Follower");
    public static readonly RaftNodeStatus Candidate = new (new Guid("{BC46D901-FDE0-4D54-8E7F-78129627138C}"), "Candidate");
}
