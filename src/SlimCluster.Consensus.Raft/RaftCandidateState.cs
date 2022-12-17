namespace SlimCluster.Consensus.Raft;

public class RaftCandidateState
{
    public int Term { get; protected set; }
    public ISet<string> RecivedVotesFrom { get; protected set; }
    public DateTimeOffset ElectionTimeout { get; protected set; }

    public RaftCandidateState(int term, DateTimeOffset electionTimeout)
    {
        Term = term;
        RecivedVotesFrom = new HashSet<string>();
        ElectionTimeout = electionTimeout;
    }

    public void AddVote(string nodeId)
    {
        RecivedVotesFrom.Add(nodeId);
    }
}