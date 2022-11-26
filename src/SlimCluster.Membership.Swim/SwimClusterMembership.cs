namespace SlimCluster.Membership.Swim;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Serialization;

/// <summary>
/// The SWIM algorithm implementation of <see cref="IClusterMembership"/> for maintaining membership.
/// </summary>
public class SwimClusterMembership : IClusterMembership, IAsyncDisposable, IMembershipEventListener
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SwimClusterMembership> _logger;
    private readonly SwimClusterMembershipOptions _options;
    private readonly ISerializer _serializer;
    private readonly ITime _time;
    private readonly Random _random = new();

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

    public string ClusterId => _options.ClusterId;

    public IMember SelfMember => _selfMember;
    public IReadOnlyCollection<IMember> OtherMembers => _otherMembers;
    public IReadOnlyCollection<IMember> Members { get; protected set; }

    public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
    public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
    public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;
    public event IClusterMembership.MemberChangedEventHandler? MemberChanged;

    /// <summary>
    /// The messaging endpoint (duplex communication)
    /// </summary>
    private MessageEndpoint? _messageEndpoint;

    /// <summary>
    /// The gossip component.
    /// </summary>
    private SwimGossip? _gossip;

    /// <summary>
    /// The protocol period loop (failure detection).
    /// </summary>
    private SwimFailureDetector? _failureDetector;

    public SwimClusterMembership(ILoggerFactory loggerFactory, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer, ITime time)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SwimClusterMembership>();
        _serializer = serializer;
        _time = time;
        _options = options.Value;

        _indirectPingRequests = new SnapshottedReadOnlyList<IndirectPingRequest>();

        _selfMember = new SwimMemberSelf(_options.NodeId, IPEndPointAddress.Unknown, time, loggerFactory);
        _otherMembers = new SnapshottedReadOnlyList<SwimMember>();
        _otherMembers.Changed += (list) => Members = new HashSet<IMember>(list) { _selfMember };

        Members = new HashSet<IMember>(_otherMembers) { _selfMember };
    }

    private bool _isStarted = false;

    public async Task Start()
    {
        if (!_isStarted)
        {
            _logger.LogInformation("Cluster membership protocol (SWIM) starting for {NodeId}...", _selfMember.Id);

            _gossip = new SwimGossip(
                _loggerFactory.CreateLogger<SwimGossip>(),
                _options,
                this,
                new MembershipEventBuffer(_options.MembershipEventBufferCount));

            _messageEndpoint = new MessageEndpoint(
                _loggerFactory.CreateLogger<MessageEndpoint>(),
                _options,
                _serializer,
                (msg, endPoint) => _gossip?.OnMessageSending(msg),
                OnMessageArrived);

            _failureDetector = new SwimFailureDetector(
                _loggerFactory.CreateLogger<SwimFailureDetector>(),
                _options,
                _messageEndpoint,
                _otherMembers,
                _time);

            _isStarted = true;

            _logger.LogInformation("Cluster membership protocol (SWIM) started for {NodeId}", _selfMember.Id);

            await NotifySelfJoined();
        }
    }

    public async Task Stop()
    {
        if (_isStarted)
        {
            _logger.LogInformation("Cluster membership protocol (SWIM) stopping for {NodeId}...", _selfMember.Id);

            await NotifySelfLeft();

            if (_failureDetector != null)
            {
                await _failureDetector.DisposeAsync();
                _failureDetector = null;
            }

            if (_messageEndpoint != null)
            {
                await _messageEndpoint.DisposeAsync();
                _messageEndpoint = null;
            }

            if (_gossip != null)
            {
                _gossip = null;
            }

            _isStarted = false;

            _logger.LogInformation("Cluster membership protocol (SWIM) stopped for {NodeId}", _selfMember.Id);
        }
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
        return SendToMulticastGroup(new NodeMessage { NodeJoined = new NodeJoinedMessage(_selfMember.Id) }, nameof(NodeJoinedMessage));
    }

    protected Task NotifySelfLeft()
    {
        // ToDo: Improvement - send message about leaving to N randomly selected nodes.

        // Announce (multicast) to others that this node left the network
        return SendToMulticastGroup(new NodeMessage { NodeLeft = new NodeLeftMessage(_selfMember.Id) }, nameof(NodeLeftMessage));
    }

    private Task SendToMulticastGroup(NodeMessage message, string messageType)
    {
        var endPoint = new IPEndPoint(_messageEndpoint!.MulticastGroupAddress, _options.Port);
        _logger.LogInformation("Sending {MessageType} for node {NodeId} on the multicast group {MulticastEndPoint}", messageType, _selfMember.Id, endPoint);
        return _messageEndpoint!.SendMessage(message, endPoint);
    }

    private async Task OnMessageArrived(NodeMessage msg, IPEndPoint remoteEndPoint)
    {
        if (msg.NodeJoined != null)
        {
            await OnNodeJoined(msg.NodeJoined.NodeId, remoteEndPoint, addToEventBuffer: true);
        }

        if (msg.NodeWelcome != null)
        {
            await OnNodeWelcome(msg.NodeWelcome, remoteEndPoint);
        }

        if (msg.NodeLeft != null)
        {
            await OnNodeLeft(msg.NodeLeft.NodeId, addToEventBuffer: true);
        }

        if (msg.Ping != null)
        {
            await OnPing(msg.Ping, remoteEndPoint);
        }

        if (msg.PingReq != null)
        {
            await OnPingReq(msg.PingReq, remoteEndPoint);
        }

        if (msg.Ack != null)
        {
            await OnPingAck(msg.Ack, remoteEndPoint);
        }

        // process gossip events
        if (_gossip != null)
        {
            await _gossip.OnMessageArrived(msg);
        }
    }

    protected SwimMember CreateMember(string nodeId, IPEndPointAddress endPoint)
        => new(nodeId, endPoint, _time.Now, SwimMemberStatus.Active, OnMemberStatusChanged, _loggerFactory.CreateLogger<SwimMember>());

    public Task OnNodeJoined(string nodeId, IPEndPoint senderEndPoint)
        => OnNodeJoined(nodeId, senderEndPoint, addToEventBuffer: false);

    protected Task OnNodeJoined(string nodeId, IPEndPoint senderEndPoint, bool addToEventBuffer)
    {
        // Add other members only
        if (nodeId != _selfMember.Id)
        {
            var member = _otherMembers.SingleOrDefault(x => x.Id == nodeId);
            if (member == null)
            {
                _logger.LogInformation("Node {NodeId} joined at {NodeEndPoint}", nodeId, senderEndPoint);

                member = CreateMember(nodeId, new IPEndPointAddress(senderEndPoint));
                _otherMembers.Mutate(list => list.Add(member));

                if (addToEventBuffer)
                {
                    // Notify the member joined
                    _gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Joined, _time.Now) { NodeAddress = member.Address.ToString() });
                }

                if (_options.WelcomeMessage.IsEnabled)
                {
                    // Send welcome

                    // If random, toss a coin - to avoid every node sending the same memberlist data
                    if (!_options.WelcomeMessage.IsRandom || _random.Next(2) == 1)
                    {
                        // Fire & forget
                        _ = SendWelcome(nodeId, senderEndPoint);
                    }
                }

                _ = NotifyMemberJoined(member);
            }
        }
        else
        {
            // Record the observed external IP address for self
            if (_selfMember.OnObservedAddress(senderEndPoint))
            {
                _ = NotifyMemberChanged(_selfMember);
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
                _logger.LogInformation("Node {NodeId} left at {NodeEndPoint}", nodeId, member.Address.EndPoint);
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

    private Task SendWelcome(string nodeId, IPEndPoint endPoint)
    {
        var message = new NodeMessage
        {
            NodeWelcome = new NodeWelcomeMessage
            {
                NodeId = _selfMember.Id,
                Nodes = _otherMembers
                    .Where(x => x.SwimStatus.IsActive) // only active members
                    .Where(x => x.Node.Id != nodeId) // exclude the newly joined node
                    .Select(x => new ActiveNode
                    {
                        NodeId = x.Node.Id,
                        NodeAddress = x.Node.Address.ToString(),
                    })
                    .ToList()
            }
        };

        _logger.LogDebug("Sending Welcome to {NodeId} on {NodeEndPoint} with {NodeCount} known members (including self)", nodeId, endPoint, message.NodeWelcome.Nodes.Count + 1);

        return _messageEndpoint!.SendMessage(message, endPoint);
    }

    protected Task OnNodeWelcome(NodeWelcomeMessage m, IPEndPoint senderEndPoint)
    {
        _logger.LogInformation("Recieved starting list of nodes ({NodeCount}) from {NodeEndPoint}", m.Nodes.Count + 1, senderEndPoint);

        _otherMembers.Mutate(list =>
        {
            foreach (var node in m.Nodes)
            {
                if (node.NodeId != _selfMember.Id)
                {
                    if (list.All(x => x.Id != node.NodeId))
                    {
                        var member = CreateMember(node.NodeId, IPEndPointAddress.Parse(node.NodeAddress));
                        list.Add(member);
                    }
                }
                else
                {
                    // Record the observed external IP address for self
                    _selfMember.OnObservedAddress(senderEndPoint);
                }
            }

            // Add the sender member (not included in the welcome message node list)
            if (list.All(x => x.Id != m.NodeId))
            {
                var member = CreateMember(m.NodeId, new IPEndPointAddress(senderEndPoint));
                list.Add(member);
            }
        });

        // ToDo: Invoke an event hook

        return Task.CompletedTask;
    }

    protected Task OnPing(PingMessage m, IPEndPoint senderEndPoint)
        => SendAck(m, senderEndPoint);

    private Task SendAck(IHasPeriodSequenceNumber m, IPEndPoint senderEndPoint, string? nodeId = null)
    {
        var message = new NodeMessage
        {
            Ack = new AckMessage
            {
                NodeId = nodeId ?? _selfMember.Id,
                PeriodSequenceNumber = m.PeriodSequenceNumber,
            }
        };

        _logger.LogDebug("Sending Ack with node {NodeId}, period sequence number {PeriodSequenceNumber} to remote {NodeEndPoint}", message.Ack.NodeId, message.Ack.PeriodSequenceNumber, senderEndPoint);

        return _messageEndpoint!.SendMessage(message, senderEndPoint);
    }

    protected Task OnPingReq(PingReqMessage m, IPEndPoint senderEndPoint)
    {
        var targetNodeEndpoint = IPEndPointAddress.Parse(m.NodeAddress).EndPoint;

        // Record PingReq in local list with expiration
        var indirectPingRequest = new IndirectPingRequest(
            periodSequenceNumber: m.PeriodSequenceNumber,
            targetEndpoint: targetNodeEndpoint,
            requestingEndpoint: senderEndPoint,
            expiresAt: _time.Now.Add(_options.ProtocolPeriod.Multiply(3))); // Waiting 3 protocol period cycles should be more than enough

        _indirectPingRequests.Mutate(list =>
        {
            // Recycle expired entries
            var now = _time.Now;
            list.RemoveAll(x => x.ExpiresAt > now);

            // Add the current request
            list.Add(indirectPingRequest);
        });

        var message = new NodeMessage
        {
            Ping = new PingMessage
            {
                PeriodSequenceNumber = m.PeriodSequenceNumber
            }
        };

        _logger.LogDebug("Sending indirect Ping with period sequence number {PeriodSequenceNumber} to remote {NodeEndPoint}", m.PeriodSequenceNumber, targetNodeEndpoint);

        return _messageEndpoint!.SendMessage(message, targetNodeEndpoint);
    }

    protected async Task OnPingAck(AckMessage m, IPEndPoint senderEndPoint)
    {
        if (_failureDetector == null)
        {
            // This is stopping (disposing).
            return;
        }

        // Forward acks for PingReq (if any)
        var matchedIndirectPingRequests = _indirectPingRequests.Where(x => x.TargetEndpoint == senderEndPoint && x.PeriodSequenceNumber == m.PeriodSequenceNumber).ToList();
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
            await Task.WhenAll(matchedIndirectPingRequests.Select(pr => SendAck(m, pr.RequestingEndpoint, nodeId: m.NodeId)));
        }

        // Let the protocol period know that an Ack arrived
        await _failureDetector.OnPingAckArrived(m, senderEndPoint);
    }
}
