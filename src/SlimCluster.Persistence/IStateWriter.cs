namespace SlimCluster.Persistence;

public interface IStateWriter
{
    void Set<T>(string key, T value);

    IStateWriter SubComponent(string key);
}
