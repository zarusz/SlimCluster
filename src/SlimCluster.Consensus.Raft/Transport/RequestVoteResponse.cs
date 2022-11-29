namespace SlimCluster.Consensus.Raft;

public class RequestVoteResponse
{
    public bool VoteGranted { get; set; }
    public int Term { get; set; }
}
