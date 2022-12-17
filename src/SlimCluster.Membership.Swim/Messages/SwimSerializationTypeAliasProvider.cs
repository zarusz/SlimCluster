namespace SlimCluster.Membership.Swim.Messages;

using SlimCluster.Serialization;

internal class SwimSerializationTypeAliasProvider : ISerializationTypeAliasProvider
{
    public IReadOnlyDictionary<string, Type> GetTypeAliases() => new Dictionary<string, Type>
    {
        ["swim-p"] = typeof(PingMessage),
        ["swim-pr"] = typeof(PingReqMessage),
        ["swim-a"] = typeof(AckMessage),
        ["swim-nj"] = typeof(NodeJoinedMessage),
        ["swim-nl"] = typeof(NodeLeftMessage)
    };
}
