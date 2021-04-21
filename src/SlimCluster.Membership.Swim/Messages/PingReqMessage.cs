namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public class PingReqMessage : PingMessage
    {
        /// <summary>
        /// Node address that sent this Ping
        /// </summary>
        [JsonProperty("ta")]
        public string TargetNodeAddress { get; set; } = string.Empty;

        /// <summary>
        /// Node port that sent this Ping
        /// </summary>
        [JsonProperty("tp")]
        public int TargetNodePort { get; set; }
    }
}
