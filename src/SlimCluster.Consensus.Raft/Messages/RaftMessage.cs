namespace SlimCluster.Consensus.Raft;

public abstract class RaftMessage : IHasRequestId
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
}
