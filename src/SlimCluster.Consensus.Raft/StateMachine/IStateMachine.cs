namespace SlimCluster.Consensus.Raft;

public interface IStateMachine
{
    Task Apply(IEnumerable<object> commands);

    // ToDo: Rebuild from snapshot
}
