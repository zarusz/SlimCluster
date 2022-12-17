namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;

public interface IHasFromNodeId
{
    [JsonProperty("fnid")]
    string FromNodeId { get; set; }
}
