namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class NodeMessage
{
    [JsonProperty("nj")]
    public NodeJoinedMessage? NodeJoined { get; set; }

    [JsonProperty("nw")]
    public NodeWelcomeMessage? NodeWelcome { get; set; }

    [JsonProperty("nl")]
    public NodeLeftMessage? NodeLeft { get; set; }

    [JsonProperty("p")]
    public PingMessage? Ping { get; set; }

    [JsonProperty("pr")]
    public PingReqMessage? PingReq { get; set; }

    [JsonProperty("a")]
    public AckMessage? Ack { get; set; }

    /// <summary>
    /// Events that the member have observed (gossip / infection style updates).
    /// </summary>
    [JsonProperty("ev")]
    public IEnumerable<MembershipEvent>? Events { get; set; }
}
