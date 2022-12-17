namespace SlimCluster.Consensus.Raft;

public class RequestVoteResponse : RaftResponse
{
    /// <summary>
    /// True means candidate received vote.
    /// </summary>
    public bool VoteGranted { get; set; }
    /// <summary>
    /// Current term for candidate to update itself
    /// </summary>
    public int Term { get; set; }

    protected RequestVoteResponse()
    {
    }

    public RequestVoteResponse(RaftMessage request) : base(request)
    {
    }

    public override string ToString() => $"{GetType().Name}(Term={Term},VoteGranted={VoteGranted})";
}
