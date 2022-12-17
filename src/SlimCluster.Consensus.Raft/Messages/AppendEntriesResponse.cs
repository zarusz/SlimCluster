namespace SlimCluster.Consensus.Raft;

public class AppendEntriesResponse : RaftResponse
{
    /// <summary>
    /// True if followe contained entry matching prevLogIndex.
    /// </summary>
    public bool Success { get; set; }
    /// <summary>
    /// Current term for leader to update itself.
    /// </summary>
    public int Term { get; set; }

    protected AppendEntriesResponse()
    {
    }

    public AppendEntriesResponse(RaftMessage request) : base(request)
    {
    }

    public override string ToString() => $"{GetType().Name}(Term={Term},Success={Success})";
}
