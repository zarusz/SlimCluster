namespace SlimCluster.Persistence;

public interface IStateReader
{
    T? Get<T>(string key);

    IStateReader SubComponent(string key);
}
