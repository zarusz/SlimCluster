namespace SlimCluster.Consensus.Raft;

public class RequestVoteRequest
{
    public int Term { get; set; }
    public string? CandidateId { get; set; }
    public int LastLogIndex { get; set; }
    public int LasLogTerm { get; set; }
}
