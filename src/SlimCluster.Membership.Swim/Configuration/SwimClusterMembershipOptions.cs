namespace SlimCluster.Membership.Swim
{
    using System;
    using System.Net.Sockets;

    public class SwimClusterMembershipOptions
    {
        /// <summary>
        /// The logical cluster ID that identifies this cluster.
        /// </summary>
        public string ClusterId { get; set; } = "MyClusterId";

        /// <summary>
        /// The unique ID representing this node instance.
        /// </summary>
        public string NodeId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// UDP port used for internal membership message exchange
        /// </summary>
        public int Port { get; set; } = 60001;

        // ToDo: Interface index
        // ToDo: IP to bind to
        public AddressFamily AddressFamily { get; set; } = AddressFamily.InterNetwork;

        /// <summary>
        /// UDP multicast group on which new joining members will announce themselves.
        /// </summary>
        public string MulticastGroupAddress { get; set; } = "239.1.1.1"; //"FF01::1";

        /// <summary>
        /// The time period (T') at which the failure detection happen. This has to be long enough to allow for ping-ack round trips to happen (at least 3x the network round trip time).
        /// </summary>
        public TimeSpan ProtocolPeriod { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The sub-group size for the failure detection (k). This indicates how many nodes are pinged in every T cycle when the direct ping to a node is unsuccessful.
        /// </summary>
        public int FailureDetectionSubgroupSize { get; set; } = 3;

        /// <summary>
        /// The period within an Ack is expected to arrive for a Ping message sent from this node to a random chosen node selected for probing during a protocol period.
        /// </summary>
        public TimeSpan PingAckTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// The timer interval that is responsible for doing checks within a protocol period (T) - if Ping Timeouts occured. Implementation setting (not SWIM related).
        /// </summary>
        public TimeSpan PeriodTimerInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Welcome message options. Welcome messages are sent to newly joined members from the perspective of this node.
        /// </summary>
        public WelecomeMessageOptions WelcomeMessage { get; set; } = new WelecomeMessageOptions();

        /// <summary>
        /// Count of the local buffer of membership events for this node.
        /// </summary>
        public int MembershipEventBufferCount { get; set; } = 20;

        /// <summary>
        /// Count of the number of events that are piggybacked for a single Ping or Ack message.
        /// </summary>
        public int MembershipEventPiggybackCount { get; set; } = 3;
    }
}
