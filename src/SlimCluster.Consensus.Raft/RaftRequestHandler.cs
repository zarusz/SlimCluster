namespace SlimCluster.Consensus.Raft;

public class RaftClientRequestHandler : IRaftClientRequestHandler
{
    private readonly RaftNode _raftNode;

    public RaftClientRequestHandler(RaftNode raftNode) => _raftNode = raftNode;

    public Task<object?> OnClientRequest(object command, CancellationToken token) => _raftNode.OnClientRequest(command, token);
}