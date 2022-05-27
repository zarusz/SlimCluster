namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;
    using System;

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class MembershipEvent : IHasNodeId, IHasTimestamp
    {
        [JsonProperty("eid")]
        public Guid EventId { get; set; }

        public string NodeId { get; set; } = string.Empty;

        [JsonProperty("na")]
        public string? NodeAddress { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        [JsonProperty("typ")]
        public MembershipEventType Type { get; set; }

        #region equality

        public override bool Equals(object? obj) => obj is MembershipEvent @event && EventId.Equals(@event.EventId);
        public override int GetHashCode() => HashCode.Combine(EventId);

        #endregion

        public MembershipEvent()
        {
        }

        public MembershipEvent(string nodeId, MembershipEventType type, DateTimeOffset timestamp)
        {
            EventId = Guid.NewGuid();
            NodeId = nodeId;
            Timestamp = timestamp;
            Type = type;
        }
    }
}
