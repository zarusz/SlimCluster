namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class NodeMessage
    {
        [JsonProperty("nj")]
        public NodeJoinedMessage? NodeJoined { get; set; }

        [JsonProperty("nl")]
        public NodeLeftMessage? NodeLeft { get; set; }

        [JsonProperty("p")]
        public PingMessage? Ping { get; set; }

        [JsonProperty("pr")]
        public PingReqMessage? PingReq { get; set; }

        [JsonProperty("a")]
        public AckMessage? Ack { get; set; }
    }
}
