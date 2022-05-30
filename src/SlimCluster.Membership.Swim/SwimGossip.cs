namespace SlimCluster.Membership.Swim;

using SlimCluster.Membership.Swim.Messages;

public class SwimGossip
{
    private readonly ILogger<SwimGossip> _logger;
    private readonly SwimClusterMembershipOptions _options;
    private readonly IMembershipEventListener _membershipEventListener;
    private readonly IMembershipEventBuffer _membershipEventBuffer;

    public IMembershipEventBuffer MembershipEventBuffer => _membershipEventBuffer;

    public SwimGossip(ILogger<SwimGossip> logger, SwimClusterMembershipOptions options, IMembershipEventListener clusterMembershipEventListener, IMembershipEventBuffer membershipEventBuffer)
    {
        _logger = logger;
        _options = options;
        _membershipEventListener = clusterMembershipEventListener;
        _membershipEventBuffer = membershipEventBuffer;
    }

    public void OnMessageSending(NodeMessage m)
    {
        if (m.Ping != null || m.Ack != null)
        {
            // piggy back on ping and ack messages to send gossip events
            m.Events = _membershipEventBuffer.GetNextEvents(_options.MembershipEventPiggybackCount);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var e in m.Events)
                {
                    _logger.LogDebug("Adding event to outgoing message about node {NodeId} ({NodeAddress}) of type {EventType}", e.NodeId, e.NodeAddress, e.Type);
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
            if (_membershipEventBuffer.Add(e))
            {
                if (e.Type == MembershipEventType.Joined)
                {
                    _logger.LogDebug("Event arrived that node {NodeId} joined on address {NodeAddress}", e.NodeId, e.NodeAddress);

                    if (e.NodeAddress == null)
                    {
                        throw new ArgumentNullException($"{nameof(e.NodeAddress)} needs to be provided for event type {e.Type}");
                    }
                    var address = IPEndPointAddress.Parse(e.NodeAddress);
                    await _membershipEventListener.OnNodeJoined(e.NodeId, address.EndPoint);
                }
                if (e.Type == MembershipEventType.Left || e.Type == MembershipEventType.Faulted)
                {
                    _logger.LogDebug("Event arrived that node {NodeId} left/faulted", e.NodeId);
                    await _membershipEventListener.OnNodeLeft(e.NodeId);
                }
            }
        }
    }
}
