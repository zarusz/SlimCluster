namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using SlimCluster.Membership.Swim.Messages;
    using System;
    using System.Threading.Tasks;

    public class SwimGossip
    {
        private readonly ILogger<SwimGossip> logger;
        private readonly SwimClusterMembershipOptions options;
        private readonly SwimClusterMembership cluster;
        private readonly SwimMembershipEventBuffer membershipEventBuffer;

        public SwimMembershipEventBuffer MembershipEventBuffer => membershipEventBuffer;

        public SwimGossip(ILogger<SwimGossip> logger, SwimClusterMembershipOptions options, SwimClusterMembership cluster)
        {
            this.logger = logger;
            this.options = options;
            this.cluster = cluster;
            this.membershipEventBuffer = new SwimMembershipEventBuffer(options.MembershipEventBufferCount);
        }

        public void OnMessageSending(NodeMessage m)
        {
            if (m.Ping != null || m.Ack != null)
            {
                // piggy back on ping and ack messages to send gossip events
                m.Events = membershipEventBuffer.GetNextEvents(options.MembershipEventPiggybackCount);

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    foreach (var e in m.Events)
                    {
                        logger.LogDebug("Adding event to outgoing message about node {NodeId} ({NodeAddress}) of type {EventType}", e.NodeId, e.NodeAddress, e.Type);
                    }
                }
            }
        }

        public async Task OnMessageArrived(NodeMessage m)
        {
            if (m.Events == null)
            {
                return;
            }

            foreach (var e in m.Events)
            {
                // process only if this is trully a new event (we did not observe it before)
                if (membershipEventBuffer.Add(e))
                {
                    if (e.Type == MembershipEventType.Joined)
                    {
                        logger.LogDebug("Event arrived that node {NodeId} joined on address {NodeAddress}", e.NodeId, e.NodeAddress);

                        if (e.NodeAddress == null)
                        {
                            throw new ArgumentNullException($"{nameof(e.NodeAddress)} needs to be provided for event type {e.Type}");
                        }
                        var address = IPEndPointAddress.Parse(e.NodeAddress);
                        await cluster.OnNodeJoined(e.NodeId, address.EndPoint);
                    }
                    if (e.Type == MembershipEventType.Left || e.Type == MembershipEventType.Faulted)
                    {
                        logger.LogDebug("Event arrived that node {NodeId} left/faulted", e.NodeId);
                        await cluster.OnNodeLeft(e.NodeId);
                    }
                }
            }
        }
    }
}
