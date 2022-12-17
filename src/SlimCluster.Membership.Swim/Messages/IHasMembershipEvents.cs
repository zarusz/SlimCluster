namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;

public interface IHasMembershipEvents
{
    /// <summary>
    /// Events that the member have observed (gossip / infection style updates).
    /// </summary>
    [JsonProperty("ev")]
    IEnumerable<MembershipEvent>? Events { get; set; }
}
