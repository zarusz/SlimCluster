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

    public void OnMessageSending(SwimMessage message)
    {
        if (message is not IHasMembershipEvents m)
        {
            return;
        }

        // piggy back on ping and ack messages to send gossip events
        m.Events = _membershipEventBuffer.GetNextEvents(_options.MembershipEventPiggybackCount);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var e in m.Events)
            {
                _logger.LogDebug("Adding event {EventType} about member {NodeId}@{NodeAddress} to outgoing message", e.Type, e.NodeId, e.NodeAddress);
            }
        }
    }

    public async Task OnMessageArrived(SwimMessage message, IAddress remoteAddress)
    {
        if (message is not IHasMembershipEvents m || m.Events == null)
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
                    _logger.LogDebug("Event arrived that member {NodeId}@{NodeAddress} joined", e.NodeId, e.NodeAddress);

                    if (e.NodeAddress == null)
                    {
                        _logger.LogWarning("{FieldName} needs to be provided for event type {EventType}", nameof(e.NodeAddress), e.Type);
                    }
                    else
                    {
                        await _membershipEventListener.OnNodeJoined(e.NodeId, remoteAddress.Parse(e.NodeAddress));
                    }
                }
                if (e.Type == MembershipEventType.Left || e.Type == MembershipEventType.Faulted)
                {
                    _logger.LogDebug("Event arrived that member {NodeId} left/faulted", e.NodeId);
                    await _membershipEventListener.OnNodeLeft(e.NodeId);
                }
            }
        }
    }
}
