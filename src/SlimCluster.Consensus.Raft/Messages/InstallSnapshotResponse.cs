namespace SlimCluster.Consensus.Raft;

public class InstallSnapshotResponse : RaftResponse
{
    public int Term { get; set; }

    protected InstallSnapshotResponse()
    {
    }

    public InstallSnapshotResponse(RaftMessage request) : base(request)
    {
    }
}
