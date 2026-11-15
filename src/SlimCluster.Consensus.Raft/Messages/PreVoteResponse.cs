namespace SlimCluster.Consensus.Raft;

internal sealed class PreVoteResponse : RaftResponse
{
    public int CurrentTerm { get; set; }
    public bool VoteGranted { get; set; }

    private PreVoteResponse()
    {
    }

    public PreVoteResponse(PreVoteRequest request) : base(request)
    {
    }
}
