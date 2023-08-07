namespace SlimCluster.Membership.Swim;

public class SwimClusterMembershipOptions
{
    /// <summary>
    /// The time period (T') at which the failure detection happen. This has to be long enough to allow for ping-ack round trips to happen (at least 3x the network round trip time).
    /// </summary>
    public TimeSpan ProtocolPeriod { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The sub-group size for the failure detection (k). This indicates how many nodes are pinged in every T cycle when the direct ping to a node is unsuccessful.
    /// </summary>
    public int FailureDetectionSubgroupSize { get; set; } = 3;

    /// <summary>
    /// The period within an Ack is expected to arrive for a Ping message sent from this node to a random chosen node selected for probing during a protocol period.
    /// </summary>
    public TimeSpan PingAckTimeout { get; set; } = TimeSpan.FromSeconds(1.25);

    /// <summary>
    /// Count of the local buffer of membership events for this node.
    /// </summary>
    public int MembershipEventBufferCount { get; set; } = 20;

    /// <summary>
    /// Count of the number of events that are piggybacked for a single Ping or Ack message.
    /// </summary>
    public int MembershipEventPiggybackCount { get; set; } = 3;
}
