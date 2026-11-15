namespace SlimCluster.Consensus.Raft;

public interface IRaftVoterReconfiguration
{
    /// <summary>
    /// Requests a new voter set on the leader and waits for the final configuration to commit.
    /// If the wait times out or is canceled, the change may still complete.
    /// </summary>
    Task ChangeVoters(IReadOnlyCollection<string> voterIds, CancellationToken token = default);
}
