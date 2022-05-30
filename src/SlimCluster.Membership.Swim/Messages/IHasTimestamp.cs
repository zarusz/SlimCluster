namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public interface IHasTimestamp
{
    [JsonProperty("ts")]
    [JsonConverter(typeof(UnixDateTimeConverter))]
    DateTimeOffset Timestamp { get; set; }
}
