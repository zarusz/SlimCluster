namespace SlimCluster.Samples.ConsoleApp.State.Logs;

using SlimCluster.Serialization;

internal class CommandSerializationTypeAliasProvider : ISerializationTypeAliasProvider
{
    public IReadOnlyDictionary<string, Type> GetTypeAliases() => new Dictionary<string, Type>
    {
        ["dec"] = typeof(DecrementCounterCommand),
        ["inc"] = typeof(IncrementCounterCommand),
        ["rst"] = typeof(ResetCounterCommand),
    };
}