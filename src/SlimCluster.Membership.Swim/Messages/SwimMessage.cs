namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public abstract class SwimMessage : IHasFromNodeId
{
    public string FromNodeId { get; set; } = string.Empty;

    protected SwimMessage()
    {
    }

    public SwimMessage(string fromNodeId) => FromNodeId = fromNodeId;
}
