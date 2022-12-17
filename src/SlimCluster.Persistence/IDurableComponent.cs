namespace SlimCluster.Persistence;

public interface IDurableComponent
{
    void OnStateRestore(IStateReader state);
    void OnStatePersist(IStateWriter state);
}
