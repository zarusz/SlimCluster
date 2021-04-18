namespace SlimCluster.Strategy.Raft
{
    public class RequestVoteResponse
    {
        public bool VoteGranted { get; set; }
        public int Term { get; set; }
    }

}
