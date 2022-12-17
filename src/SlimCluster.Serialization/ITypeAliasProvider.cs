namespace SlimCluster.Serialization;

public interface ISerializationTypeAliasProvider
{
    IReadOnlyDictionary<string, Type> GetTypeAliases();
}
