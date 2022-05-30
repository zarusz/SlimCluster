namespace SlimCluster.Membership.Swim;

using Microsoft.Extensions.Options;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Serialization;

/// <summary>
/// The SWIM algorithm implementation of <see cref="IClusterMembership"/> for maintaining membership.
/// </summary>
public class SwimClusterMembership : IClusterMembership, IAsyncDisposable, IMessageSender, IMembershipEventListener
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
    /// The gossip component.
    /// </summary>
    private SwimGossip? gossip;

    /// <summary>
    /// The protocol period loop (failure detection).
    /// </summary>
    private SwimFailureDetector? failureDetector;

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

        _multicastGroupAddress = IPAddress.Parse(_options.MulticastGroupAddress);
    }

    private readonly object udpClientLock = new();
    private UdpClient? udpClient;
    private readonly IPAddress _multicastGroupAddress;

    private bool isStarted = false;
    private CancellationTokenSource? loopCts;

    private Task? recieveLoopTask;

    public async Task Start()
    {
        if (!isStarted)
        {
            _logger.LogInformation("Cluster membership protocol (SWIM) starting...");

            lock (udpClientLock)
            {
                //NetworkInterface.GetAllNetworkInterfaces()
                //var ip = IPAddress.Parse("192.168.100.50");
                //var ep = new IPEndPoint(ip, options.UdpPort);
                //udpClient = new UdpClient(ep);
                // Join or create a multicast group
                udpClient = new UdpClient(_options.Port, _options.AddressFamily);
                // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0
                udpClient.JoinMulticastGroup(_multicastGroupAddress);
            }

            _logger.LogInformation("Node listening on {NodeEndPoint}", udpClient.Client.LocalEndPoint);

            loopCts = new CancellationTokenSource();

            // Run the message processing loop
            recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning);

            gossip = new SwimGossip(_loggerFactory.CreateLogger<SwimGossip>(), _options, this, new MembershipEventBuffer(_options.MembershipEventBufferCount));
            failureDetector = new SwimFailureDetector(_loggerFactory.CreateLogger<SwimFailureDetector>(), _options, this, _otherMembers, _time);

            isStarted = true;

            _logger.LogInformation("Cluster membership protocol (SWIM) started");

            await NotifySelfJoined();
        }
    }

    public async Task Stop()
    {
        if (isStarted)
        {
            _logger.LogInformation("Cluster membership protocol (SWIM) stopping...");

            loopCts?.Cancel();

            if (failureDetector != null)
            {
                await failureDetector.DisposeAsync();
                failureDetector = null;
            }

            if (recieveLoopTask != null)
            {
                try
                {
                    await recieveLoopTask;
                }
                catch
                {
                }
                recieveLoopTask = null;
            }

            await NotifySelfLeft();

            if (gossip != null)
            {
                gossip = null;
            }

            // Stop multicast group
            lock (udpClientLock)
            {
                if (udpClient != null)
                {
                    udpClient.DropMulticastGroup(_multicastGroupAddress);
                    udpClient.Dispose();
                    udpClient = null;
                }
            }

            if (loopCts != null)
            {
                loopCts.Dispose();
                loopCts = null;
            }

            isStarted = false;

            _logger.LogInformation("Cluster membership protocol (SWIM) stopped");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
    }

    public Task SendMessage<T>(T message, IPEndPoint endPoint) where T : class
    {
        if (message is NodeMessage nodeMessage)
        {
            gossip?.OnMessageSending(nodeMessage);
        }

        var payload = _serializer.Serialize(message);
        _logger.LogTrace("Sending message to {NodeEndPoint}", endPoint);
        return udpClient?.SendAsync(payload, payload.Length, endPoint) ?? Task.CompletedTask;
    }

    protected Task NotifySelfJoined() =>
        // Announce (multicast) to others that this node joined the network
        SendToMulticastGroup(new NodeMessage { NodeJoined = new NodeJoinedMessage(_selfMember.Id) }, nameof(NodeJoinedMessage));

    protected Task NotifySelfLeft() =>
        // Announce (multicast) to others that this node left the network
        SendToMulticastGroup(new NodeMessage { NodeLeft = new NodeLeftMessage(_selfMember.Id) }, nameof(NodeLeftMessage));

    private Task SendToMulticastGroup(NodeMessage message, string messageType)
    {
        var endPoint = new IPEndPoint(_multicastGroupAddress, _options.Port);
        _logger.LogInformation("Sending {MessageType} for node {NodeId} on the multicast group {MulticastEndPoint}", messageType, _selfMember.Id, endPoint);
        return SendMessage(message, endPoint);
    }

    private async Task RecieveLoop()
    {
        _logger.LogInformation("Recieve loop started");
        try
        {
            while (loopCts != null && !loopCts.IsCancellationRequested)
            {
                var result = await udpClient!.ReceiveAsync();
                try
                {
                    var msg = _serializer.Deserialize<NodeMessage>(result.Buffer);
                    if (msg != null)
                    {
                        await OnMessageArrived(msg, result.RemoteEndPoint);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Could not handle arriving message from remote endpoint {RemoteEndPoint}", result.RemoteEndPoint);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Intended: this is how it exists from ReceiveAsync
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Recieve loop error");
        }
        finally
        {
            _logger.LogInformation("Recieve loop finished");
        }
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
        if (gossip != null)
        {
            await gossip.OnMessageArrived(msg);
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
                    gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Joined, _time.Now) { NodeAddress = member.Address.ToString() });
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
                    gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Left, _time.Now));
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
            gossip?.MembershipEventBuffer.Add(new MembershipEvent(member.Id, MembershipEventType.Faulted, _time.Now));

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

        return SendMessage(message, endPoint);
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

        return SendMessage(message, senderEndPoint);
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

        return SendMessage(message, targetNodeEndpoint);
    }

    protected async Task OnPingAck(AckMessage m, IPEndPoint senderEndPoint)
    {
        if (failureDetector == null)
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
        await failureDetector.OnPingAckArrived(m, senderEndPoint);
    }
}
