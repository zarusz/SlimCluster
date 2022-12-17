namespace SlimCluster.Consensus.Raft;

public interface IRaftClientRequestHandler
{
    Task<object?> OnClientRequest(object command, CancellationToken token);
}
