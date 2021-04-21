namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public interface IHasNodeId
    {
        [JsonProperty("nid")]
        string NodeId { get; set; }
    }
}
