namespace SlimCluster.Consensus.Raft;

public class InstallSnapshotResponse : RaftResponse
{
    public int Term { get; set; }
    public bool Success { get; set; }

    protected InstallSnapshotResponse()
    {
    }

    public InstallSnapshotResponse(RaftMessage request) : base(request)
    {
    }
}
