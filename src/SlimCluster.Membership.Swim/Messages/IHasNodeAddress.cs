namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public interface IHasNodeAddress
    {
        /// <summary>
        /// Node address that sent this Ping
        /// </summary>
        [JsonProperty("ta")]
        string NodeAddress { get; set; }
    }
}
