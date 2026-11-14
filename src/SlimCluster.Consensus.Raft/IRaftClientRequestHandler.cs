namespace SlimCluster.Consensus.Raft;

public interface IRaftClientRequestHandler
{
    /// <exception cref="TimeoutException">The command result was not confirmed before the deadline.</exception>
    Task<object?> OnClientRequest(object command, CancellationToken token);
}
