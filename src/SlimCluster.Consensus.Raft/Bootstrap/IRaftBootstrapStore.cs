namespace SlimCluster.Consensus.Raft;

/// <summary>Publishes the initial Raft voter list and records which voters have persisted it.</summary>
public interface IRaftBootstrapStore
{
    /// <summary>
    /// Gets the published list or atomically publishes the proposed list if none exists.
    /// A null proposal only reads. Concurrent proposals must observe the same winning list.
    /// </summary>
    Task<string[]?> GetOrPublishVoters(string clusterId, string[]? proposedVoters, CancellationToken token);

    /// <summary>Durably records that this voter has persisted the published list. Calls are idempotent.</summary>
    Task MarkVoterReady(string clusterId, string voterId, CancellationToken token);

    /// <summary>Checks whether every supplied voter has confirmed local persistence.</summary>
    Task<bool> AreVotersReady(string clusterId, string[] voterIds, CancellationToken token);
}
