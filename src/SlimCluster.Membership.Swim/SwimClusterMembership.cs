namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using SlimCluster.Membership.Swim.Messages;
    using SlimCluster.Membership.Swim.Serialization;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The SWIM algorithm implementation of <see cref="IClusterMembership"/> for maintaining membership.
    /// </summary>
    public class SwimClusterMembership : IClusterMembership, IAsyncDisposable, IClusterMessageSender
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<SwimClusterMembership> logger;
        private readonly SwimClusterMembershipOptions options;
        private readonly ISerializer serializer;
        private readonly ITime time;
        private readonly SwimMembershipEventBuffer membershipEventBuffer;

        /// <summary>
        /// Other known members
        /// </summary>
        private readonly SnapshottedReadOnlyList<SwimMember> otherMembers;

        /// <summary>
        /// Member representing current node.
        /// </summary>
        private readonly SwimMemberSelf memberSelf;

        /// <summary>
        /// List of indirect ping requests that this node has been asked for handling.
        /// </summary>
        private readonly SnapshottedReadOnlyList<IndirectPingRequest> indirectPingRequests;

        public string ClusterId => options.ClusterId;

        public IReadOnlyCollection<IMember> Members => new HashSet<IMember>(otherMembers) { memberSelf };

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
        public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;

        /// <summary>
        /// The protocol period loop (failure detection).
        /// </summary>
        private SwimFailureDetector? failureDetector;

        public SwimClusterMembership(ILoggerFactory loggerFactory, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer, ITime time)
        {
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger<SwimClusterMembership>();
            this.serializer = serializer;
            this.time = time;
            this.options = options.Value;

            this.membershipEventBuffer = new SwimMembershipEventBuffer(this.options.MembershipEventBufferCount);
            this.indirectPingRequests = new SnapshottedReadOnlyList<IndirectPingRequest>();

            this.otherMembers = new SnapshottedReadOnlyList<SwimMember>();
            this.memberSelf = new SwimMemberSelf(this.options.NodeId, IPEndPointAddress.Unknown, time, loggerFactory);

            this.multicastGroupAddress = IPAddress.Parse(this.options.MulticastGroupAddress);
        }

        private readonly object udpClientLock = new();
        private UdpClient? udpClient;
        private readonly IPAddress multicastGroupAddress;

        private bool isStarted = false;
        private CancellationTokenSource? loopCts;

        private Task? recieveLoopTask;

        public async Task Start()
        {
            if (!isStarted)
            {
                logger.LogInformation("Cluster membership protocol (SWIM) starting...");

                lock (udpClientLock)
                {
                    //NetworkInterface.GetAllNetworkInterfaces()
                    //var ip = IPAddress.Parse("192.168.100.50");
                    //var ep = new IPEndPoint(ip, options.UdpPort);
                    //udpClient = new UdpClient(ep);
                    // Join or create a multicast group
                    udpClient = new UdpClient(options.Port, options.AddressFamily);
                    // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0
                    udpClient.JoinMulticastGroup(multicastGroupAddress);
                }

                logger.LogInformation("Node listening on {NodeEndPoint}", udpClient.Client.LocalEndPoint);

                loopCts = new CancellationTokenSource();

                // Run the message processing loop
                recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning);

                failureDetector = new SwimFailureDetector(loggerFactory.CreateLogger<SwimFailureDetector>(), options, this, otherMembers, time);

                isStarted = true;

                logger.LogInformation("Cluster membership protocol (SWIM) started");

                await NotifySelfJoined();
            }
        }

        public async Task Stop()
        {
            if (isStarted)
            {
                logger.LogInformation("Cluster membership protocol (SWIM) stopping...");

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

                // Stop multicast group
                lock (udpClientLock)
                {
                    if (udpClient != null)
                    {
                        udpClient.DropMulticastGroup(multicastGroupAddress);
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

                logger.LogInformation("Cluster membership protocol (SWIM) stopped");
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
                OnSendingMessage(nodeMessage);
            }

            var payload = serializer.Serialize(message);
            logger.LogTrace("Sending message to {NodeEndPoint}", endPoint);
            return udpClient?.SendAsync(payload, payload.Length, endPoint) ?? Task.CompletedTask;
        }

        protected Task NotifySelfJoined() =>
            // Announce (multicast) to others that this node joined the network
            SendToMulticastGroup(new NodeMessage { NodeJoined = new NodeJoinedMessage(memberSelf.Id) }, nameof(NodeJoinedMessage));

        protected Task NotifySelfLeft() =>
            // Announce (multicast) to others that this node left the network
            SendToMulticastGroup(new NodeMessage { NodeLeft = new NodeLeftMessage(memberSelf.Id) }, nameof(NodeLeftMessage));

        private Task SendToMulticastGroup(NodeMessage message, string messageType)
        {
            var endPoint = new IPEndPoint(multicastGroupAddress, options.Port);
            logger.LogInformation("Sending {MessageType} for node {NodeId} on the multicast group {MulticastEndPoint}", messageType, memberSelf.Id, endPoint);
            return SendMessage(message, endPoint);
        }

        private async Task RecieveLoop()
        {
            logger.LogInformation("Recieve loop started");
            try
            {
                while (loopCts != null && !loopCts.IsCancellationRequested)
                {
                    var result = await udpClient!.ReceiveAsync();
                    try
                    {
                        var msg = serializer.Deserialize<NodeMessage>(result.Buffer);
                        if (msg != null)
                        {
                            await OnMessageArrived(msg, result.RemoteEndPoint);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Could not handle arriving message from remote endpoint {RemoteEndPoint}", result.RemoteEndPoint);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Intended: this is how it exists from ReceiveAsync
            }
            catch (Exception e)
            {
                logger.LogError(e, "Recieve loop error");
            }
            finally
            {
                logger.LogInformation("Recieve loop finished");
            }
        }

        private async Task OnMessageArrived(NodeMessage msg, IPEndPoint remoteEndPoint)
        {
            if (msg.NodeJoined != null)
            {
                await OnNodeJoined(msg.NodeJoined, remoteEndPoint);
            }

            if (msg.NodeWelcome != null)
            {
                await OnNodeWelcome(msg.NodeWelcome, remoteEndPoint);
            }

            if (msg.NodeLeft != null)
            {
                await OnNodeLeft(msg.NodeLeft);
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
            if (msg.Events != null)
            {
                await OnGossipEventsArrived(msg.Events);
            }
        }

        private void OnSendingMessage(NodeMessage m)
        {
            if (m.Ping != null || m.Ack != null)
            {
                // piggy back on ping and ack messages to send gossip events
                m.Events = membershipEventBuffer.GetNextEvents(options.MembershipEventPiggybackCount);
            }
        }

        protected async Task OnGossipEventsArrived(IEnumerable<MembershipEvent> events)
        {
            foreach (var e in events)
            {
                // process only if this is trully a new event (we did not observe it before)
                if (membershipEventBuffer.Add(e))
                {
                    if (e.Type == MembershipEventType.Joined)
                    {
                        if (e.NodeAddress == null)
                        {
                            throw new ArgumentNullException($"{nameof(e.NodeAddress)} needs to be provided for event type {e.Type}");
                        }
                        var address = IPEndPointAddress.Parse(e.NodeAddress);
                        await OnNodeJoined(new NodeJoinedMessage(e.NodeId), address.EndPoint);
                    }
                    if (e.Type == MembershipEventType.Left || e.Type == MembershipEventType.Faulted)
                    {
                        await OnNodeLeft(new NodeLeftMessage(e.NodeId));
                    }
                }
            }
        }

        protected SwimMember CreateMember(string nodeId, IPEndPointAddress endPoint)
            => new(nodeId, endPoint, time.Now, SwimMemberStatus.Active, OnMemberStatusChanged, loggerFactory.CreateLogger<SwimMember>());

        protected Task OnNodeJoined(NodeJoinedMessage m, IPEndPoint senderEndPoint)
        {
            // Add other members only
            if (m.NodeId != memberSelf.Id)
            {
                var member = otherMembers.SingleOrDefault(x => x.Id == m.NodeId);
                if (member == null)
                {
                    logger.LogInformation("Node {NodeId} joined at {NodeEndPoint}", m.NodeId, senderEndPoint);

                    member = CreateMember(m.NodeId, new IPEndPointAddress(senderEndPoint));
                    otherMembers.Mutate(list => list.Add(member));

                    if (options.WelcomeMessage.IsEnabled)
                    {
                        // Send welcome

                        // If random, toss a coin - to avoid every node sending the same memberlist data
                        if (!options.WelcomeMessage.IsRandom || random.Next(2) == 1)
                        {
                            // Fire & forget
                            _ = SendWelcome(m, senderEndPoint);
                        }
                    }

                    _ = NotifyMemberJoined(member);
                }
            }
            else
            {
                // Record the observed external IP address for self
                if (memberSelf.OnObservedAddress(senderEndPoint))
                {
                    _ = NotifyMemberChanged(memberSelf);
                }
            }

            return Task.CompletedTask;
        }

        protected Task OnNodeLeft(NodeLeftMessage m)
        {
            // Add other members only
            if (m.NodeId != memberSelf.Id)
            {
                var member = otherMembers.SingleOrDefault(x => x.Id == m.NodeId);
                if (member != null)
                {
                    logger.LogInformation("Node {NodeId} left at {NodeEndPoint}", m.NodeId, member.Address.EndPoint);
                    otherMembers.Mutate(list => list.Remove(member));

                    NotifyMemberLeft(member);
                }
            }
            return Task.CompletedTask;
        }

        protected void OnMemberStatusChanged(SwimMember member)
        {
            if (member.SwimStatus == SwimMemberStatus.Faulted)
            {
                _ = OnNodeLeft(new NodeLeftMessage(member.Id));
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
                MemberStatusChanged?.Invoke(this, new MemberEventArgs(member, time.Now));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberStatusChanged));
            }
            return Task.CompletedTask;
        }

        private Task NotifyMemberJoined(SwimMember member)
        {
            try
            {
                MemberJoined?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberJoined));
            }
            return Task.CompletedTask;
        }

        private Task NotifyMemberLeft(SwimMember member)
        {
            try
            {
                MemberLeft?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberLeft));
            }
            return Task.CompletedTask;
        }

        private readonly Random random = new();

        private Task SendWelcome(NodeJoinedMessage m, IPEndPoint endPoint)
        {
            var message = new NodeMessage
            {
                NodeWelcome = new NodeWelcomeMessage
                {
                    NodeId = memberSelf.Id,
                    Nodes = otherMembers
                        .Where(x => x.SwimStatus.IsActive) // only active members
                        .Where(x => x.Node.Id != m.NodeId) // exclude the newly joined node
                        .Select(x => new ActiveNode
                        {
                            NodeId = x.Node.Id,
                            NodeAddress = x.Node.Address.ToString(),
                        })
                        .ToList()
                }
            };

            logger.LogDebug("Sending Welcome to {NodeId} on {NodeEndPoint} with {NodeCount} known members (including self)", m.NodeId, endPoint, message.NodeWelcome.Nodes.Count + 1);

            return SendMessage(message, endPoint);
        }

        protected Task OnNodeWelcome(NodeWelcomeMessage m, IPEndPoint senderEndPoint)
        {
            logger.LogInformation("Recieved starting list of nodes ({NodeCount}) from {NodeEndPoint}", m.Nodes.Count + 1, senderEndPoint);

            otherMembers.Mutate(list =>
            {
                foreach (var node in m.Nodes)
                {
                    if (node.NodeId != memberSelf.Id)
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
                        memberSelf.OnObservedAddress(senderEndPoint);
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
                    NodeId = nodeId ?? memberSelf.Id,
                    PeriodSequenceNumber = m.PeriodSequenceNumber,
                }
            };

            logger.LogDebug("Sending Ack with node {NodeId}, period sequence number {PeriodSequenceNumber} to remote {NodeEndPoint}", message.Ack.NodeId, message.Ack.PeriodSequenceNumber, senderEndPoint);

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
                expiresAt: time.Now.Add(options.ProtocolPeriod.Multiply(3))); // Waiting 3 protocol period cycles should be more than enough

            indirectPingRequests.Mutate(list =>
            {
                // Recycle expired entries
                var now = time.Now;
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

            logger.LogDebug("Sending indirect Ping with period sequence number {PeriodSequenceNumber} to remote {NodeEndPoint}", m.PeriodSequenceNumber, targetNodeEndpoint);

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
            var matchedIndirectPingRequests = indirectPingRequests.Where(x => x.TargetEndpoint == senderEndPoint && x.PeriodSequenceNumber == m.PeriodSequenceNumber).ToList();
            if (matchedIndirectPingRequests.Count > 0)
            {
                indirectPingRequests.Mutate(list =>
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
}
