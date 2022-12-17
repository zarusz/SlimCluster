namespace SlimCluster.Membership.Swim;

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SlimCluster.Host;
using SlimCluster.Host.Common;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Persistence;
using SlimCluster.Transport;

/// <summary>
/// The SWIM algorithm implementation of <see cref="IClusterMembership"/> for maintaining membership.
/// </summary>
public class SwimClusterMembership : TaskLoop, IClusterMembership, IClusterControlComponent, IAsyncDisposable, IMembershipEventListener, IMessageSendingHandler, IMessageArrivedHandler, IDurableComponent
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SwimClusterMembership> _logger;
    private readonly SwimClusterMembershipOptions _options;
    private readonly ITime _time;

    /// <summary>
    /// The messaging endpoint (duplex communication)
    /// </summary>
    private readonly IMessageSender _messageSender;

    /// <summary>
    /// The gossip component.
    /// </summary>
    private SwimGossip? _gossip;

    /// <summary>
    /// The protocol period loop (failure detection).
    /// </summary>
    private SwimFailureDetector? _failureDetector;

    /// <summary>
    /// Queue of arriving messages.
    /// </summary>
    private readonly ConcurrentQueue<(SwimMessage Message, IAddress Address)> _messages = new();

    /// <summary>
    /// Other known members
    /// </summary>
    private readonly SnapshottedReadOnlyList<SwimMember> _otherMembers;

    /// <summary>
    /// Member representing current node.
    /// </summary>
    private readonly SwimMemberSelf _selfMember;

    /// <summary>
    /// List of indirect ping requests that this node has been asked for handling.
    /// </summary>
    private readonly SnapshottedReadOnlyList<IndirectPingRequest> _indirectPingRequests;

    public string ClusterId { get; protected set; }
    public IMember SelfMember => _selfMember;
    public IReadOnlyCollection<IMember> OtherMembers => _otherMembers;
    public IReadOnlyCollection<IMember> Members { get; protected set; }

    public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
    public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
    public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;
    public event IClusterMembership.MemberChangedEventHandler? MemberChanged;

    public SwimClusterMembership(
        ILoggerFactory loggerFactory,
        IOptions<SwimClusterMembershipOptions> options,
        IOptions<ClusterOptions> clusterOptions,
        ITime time,
        IMessageSender messageSender)
        : base(loggerFactory.CreateLogger<SwimClusterMembership>())
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SwimClusterMembership>();
        _time = time;
        _messageSender = messageSender;
        _options = options.Value;

        _indirectPingRequests = new SnapshottedReadOnlyList<IndirectPingRequest>();

        _selfMember = new SwimMemberSelf(clusterOptions.Value.NodeId, UnknownAddress.Instance, time, loggerFactory);
        _otherMembers = new SnapshottedReadOnlyList<SwimMember>();
        _otherMembers.Changed += (list) => Members = new HashSet<IMember>(list) { _selfMember };

        ClusterId = clusterOptions.Value.ClusterId;
        Members = new HashSet<IMember>(_otherMembers) { _selfMember };
    }

    protected override Task OnStarting()
    {
        _logger.LogInformation("Cluster membership protocol (SWIM) starting for {NodeId} ...", _selfMember.Id);

        _gossip = new SwimGossip(
            _loggerFactory.CreateLogger<SwimGossip>(),
            _options,
            this,
            new MembershipEventBuffer(_options.MembershipEventBufferCount));

        _failureDetector = new SwimFailureDetector(
            _loggerFactory.CreateLogger<SwimFailureDetector>(),
            _options,
            _messageSender,
            _otherMembers,
            _selfMember,
            _time);

        return Task.CompletedTask;
    }

    protected override Task OnStarted()
    {
        return NotifySelfJoined();
    }

    protected override Task OnStopping()
    {
        _logger.LogInformation("Cluster membership protocol (SWIM) stopping for {NodeId} ...", _selfMember.Id);

        return NotifySelfLeft();
    }

    protected override Task OnStopped()
    {
        _failureDetector = null;
        _gossip = null;

        return Task.CompletedTask;
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        var idleRun = true;

        if (_messages.TryDequeue(out var arrivedMessage))
        {
            idleRun = false;
            await OnMessageArrived(arrivedMessage.Message, arrivedMessage.Address);
        }

        if (_failureDetector != null && !await _failureDetector.DoRun())
        {
            idleRun = false;
        }

        return idleRun;
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
        GC.SuppressFinalize(this);
    }

    protected Task NotifySelfJoined()
    {
        // ToDo: Introduce an option to either use multicast or initial list of nodes seed

        // Announce (multicast) to others that this node joined the network
        return SendToMulticastGroup(new NodeJoinedMessage(_selfMember.Id));
    }

    protected Task NotifySelfLeft()
    {
        // ToDo: Improvement - send message about leaving to N randomly selected nodes.

        // Announce (multicast) to others that this node left the network
        return SendToMulticastGroup(new NodeLeftMessage(_selfMember.Id));
    }

    private async Task SendToMulticastGroup(SwimMessage message)
    {
        if (_messageSender.MulticastGroupEndpoint != null)
        {
            _logger.LogInformation("Sending {MessageType} for node {NodeId} to the multicast group {MulticastAddress}", message.GetType().Name, _selfMember.Id, _messageSender.MulticastGroupEndpoint);
            await _messageSender.SendMessage(message, _messageSender.MulticastGroupEndpoint).ConfigureAwait(false);
        }
    }

    private async Task OnMessageArrived(SwimMessage msg, IAddress remoteAddress)
    {
        _logger.LogDebug("Processing {MessageType} from remote address {NodeAddress}", msg.GetType().Name, remoteAddress);

        var task = msg switch
        {
            NodeJoinedMessage m => OnNodeJoined(m.FromNodeId, remoteAddress, addToEventBuffer: true),
            NodeLeftMessage m => OnNodeLeft(m.FromNodeId, addToEventBuffer: true),
            PingReqMessage m => OnPingReq(m, remoteAddress),
            PingMessage m => OnPing(m, remoteAddress),
            AckMessage m => OnAck(m, remoteAddress),
            _ => null
        };

        if (task != null)
        {
            await task.ConfigureAwait(false);
        }

        // process gossip events
        if (_gossip != null)
        {
            await _gossip.OnMessageArrived(msg, remoteAddress).ConfigureAwait(false);
        }
    }

    protected SwimMember CreateMember(string nodeId, IAddress address)
        => new(nodeId, address, _time.Now, SwimMemberStatus.Active, OnMemberStatusChanged, _loggerFactory.CreateLogger<SwimMember>());

    public Task OnNodeJoined(string nodeId, IAddress senderAddress)
        => OnNodeJoined(nodeId, senderAddress, addToEventBuffer: false);

    protected Task OnNodeJoined(string nodeId, IAddress senderAddress, bool addToEventBuffer)
    {
        var member = EnsureNodeOnMemberlist(nodeId, senderAddress);

        // Add other members only
        if (member.Id != _selfMember.Id)
        {
            _logger.LogInformation("Node {NodeId} joined at {NodeAddress}", nodeId, senderAddress);

            if (addToEventBuffer)
            {
                // Notify the member joined
                _gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Joined, _time.Now) { NodeAddress = member.Address.ToString() });
            }
        }

        return Task.CompletedTask;
    }

    public Task OnNodeLeft(string nodeId)
        => OnNodeLeft(nodeId, addToEventBuffer: false);

    protected Task OnNodeLeft(string nodeId, bool addToEventBuffer)
    {
        // Add other members only
        if (nodeId != _selfMember.Id)
        {
            var member = _otherMembers.SingleOrDefault(x => x.Id == nodeId);
            if (member != null)
            {
                _logger.LogInformation("Node {NodeId} left at {NodeAddress}", nodeId, member.Address);
                _otherMembers.Mutate(list => list.Remove(member));

                if (addToEventBuffer)
                {
                    // Notify the member faulted
                    _gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Left, _time.Now));
                }

                _ = NotifyMemberLeft(member);
            }
        }
        return Task.CompletedTask;
    }

    protected void OnMemberStatusChanged(SwimMember member)
    {
        if (member.SwimStatus == SwimMemberStatus.Faulted)
        {
            // Notify the member faulted
            _gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Faulted, _time.Now));

            _ = OnNodeLeft(member.Id);
        }
        else
        {
            _ = NotifyMemberChanged(member);
        }
    }

    private Task NotifyMemberChanged(SwimMember member)
    {
        try
        {
            var e = new MemberEventArgs(member, _time.Now);
            MemberStatusChanged?.Invoke(this, e);
            MemberChanged?.Invoke(this, e);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberStatusChanged));
        }
        return Task.CompletedTask;
    }

    private Task NotifyMemberJoined(SwimMember member)
    {
        try
        {
            var e = new MemberEventArgs(member.Node, member.LastSeen);
            MemberJoined?.Invoke(this, e);
            MemberChanged?.Invoke(this, e);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberJoined));
        }
        return Task.CompletedTask;
    }

    private Task NotifyMemberLeft(SwimMember member)
    {
        try
        {
            var e = new MemberEventArgs(member.Node, member.LastSeen);
            MemberLeft?.Invoke(this, e);
            MemberChanged?.Invoke(this, e);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberLeft));
        }
        return Task.CompletedTask;
    }

    private SwimMember EnsureNodeOnMemberlist(string nodeId, IAddress nodeAddress)
    {
        _logger.LogDebug("Ensuring node {NodeId}@{NodeAddress} is on the memberlist", nodeId, nodeAddress);

        var member = nodeId == _selfMember.Id
            ? _selfMember
            : _otherMembers.SingleOrDefault(x => x.Id == nodeId);
        if (member != null)
        {
            // Check if address changed
            if (member.OnObservedAddress(nodeAddress))
            {
                _ = NotifyMemberChanged(member);
            }
        }
        else
        {
            // Try add if it's not yet on memberlist
            member = _otherMembers.Mutate(list =>
            {
                var existingMember = list.SingleOrDefault(x => x.Id == nodeId);
                if (existingMember == null)
                {
                    existingMember = CreateMember(nodeId, nodeAddress);
                    list.Add(existingMember);
                    _logger.LogTrace("Added {Node} to the memberlist", existingMember);
                }
                return existingMember;
            });

            _ = NotifyMemberJoined(member);
        }
        return member;
    }

    protected async Task OnPing(PingMessage m, IAddress senderAddress)
    {
        EnsureNodeOnMemberlist(m.FromNodeId, senderAddress);
        await SendAck(m, senderAddress);
    }

    private Task SendAck(IHasPeriodSequenceNumber m, IAddress senderAddress, string? onBehalfOfNodeId = null)
    {
        var message = new AckMessage(_selfMember.Id)
        {
            NodeId = onBehalfOfNodeId ?? _selfMember.Id,
            PeriodSequenceNumber = m.PeriodSequenceNumber,
        };
        _logger.LogDebug("Sending Ack with node {NodeId}, period sequence number {PeriodSequenceNumber} to remote {NodeAddress}", message.NodeId, message.PeriodSequenceNumber, senderAddress);
        return _messageSender.SendMessage(message, senderAddress);
    }

    protected Task OnPingReq(PingReqMessage m, IAddress senderAddress)
    {
        EnsureNodeOnMemberlist(m.FromNodeId, senderAddress);

        var targetNodeAddress = senderAddress.Parse(m.NodeAddress);

        // Record PingReq in local list with expiration
        var indirectPingRequest = new IndirectPingRequest(
            periodSequenceNumber: m.PeriodSequenceNumber,
            targetAddress: targetNodeAddress,
            requestingAddress: senderAddress,
            expiresAt: _time.Now.Add(_options.ProtocolPeriod.Multiply(3))); // Waiting 3 protocol period cycles should be more than enough

        _indirectPingRequests.Mutate(list =>
        {
            // Recycle expired entries
            var now = _time.Now;
            list.RemoveAll(x => x.ExpiresAt > now);

            // Add the current request
            list.Add(indirectPingRequest);
        });

        var message = new PingMessage(_selfMember.Id)
        {
            PeriodSequenceNumber = m.PeriodSequenceNumber
        };

        _logger.LogDebug("Sending indirect Ping with period sequence number {PeriodSequenceNumber} to remote {NodeAddress}", m.PeriodSequenceNumber, targetNodeAddress);
        return _messageSender.SendMessage(message, targetNodeAddress);
    }

    protected async Task OnAck(AckMessage m, IAddress senderAddress)
    {
        if (_failureDetector == null)
        {
            // This is stopping (disposing).
            return;
        }

        // Forward acks for PingReq (if any)
        var matchedIndirectPingRequests = _indirectPingRequests.Where(x => x.TargetEndpoint.Equals(senderAddress) && x.PeriodSequenceNumber == m.PeriodSequenceNumber).ToList();
        if (matchedIndirectPingRequests.Count > 0)
        {
            _indirectPingRequests.Mutate(list =>
            {
                foreach (var pr in matchedIndirectPingRequests)
                {
                    list.Remove(pr);
                }
            });

            // Send Acks to those nodes who requested PingReq before
            await Task.WhenAll(matchedIndirectPingRequests.Select(pr => SendAck(m, pr.RequestingEndpoint, onBehalfOfNodeId: m.NodeId)));
        }

        // Let the protocol period know that an Ack arrived
        await _failureDetector.OnAckArrived(m, senderAddress);
    }

    #region IMessageSendingHandler

    public Task OnMessageSending(object message, IAddress address)
    {
        if (message is SwimMessage swimMessage)
        {
            _gossip?.OnMessageSending(swimMessage);
        }
        return Task.CompletedTask;
    }

    #endregion

    #region IMessageArrivedHandler

    public Task OnMessageArrived(object message, IAddress address)
    {
        if (message is SwimMessage swimMessage)
        {
            _messages.Enqueue((swimMessage, address));
        }
        return Task.CompletedTask;
    }

    #endregion

    #region IMessageHandler

    // We only want to recieve notification about SWIM messages
    public bool CanHandle(object message) => message is SwimMessage;

    #endregion

    #region IDurableComponent

    public void OnStateRestore(IStateReader state)
    {
    }

    public void OnStatePersist(IStateWriter state)
    {
    }

    #endregion
}
