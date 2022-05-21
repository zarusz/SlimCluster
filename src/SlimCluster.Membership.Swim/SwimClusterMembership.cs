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

    public class IndirectPingRequest : IHasPeriodSequenceNumber
    {
        public long PeriodSequenceNumber { get; set; }
        public IPEndPoint RequestingEndpoint { get; private set; }
        public IPEndPoint TargetEndpoint { get; private set; }
        /// <summary>
        /// Time after which the request is no longer needed
        /// </summary>
        public DateTimeOffset ExpiresAt { get; private set; }

        public IndirectPingRequest(long periodSequenceNumber, IPEndPoint requestingEndpoint, IPEndPoint targetEndpoint, DateTimeOffset expiresAt)
        {
            PeriodSequenceNumber = periodSequenceNumber;
            RequestingEndpoint = requestingEndpoint;
            TargetEndpoint = targetEndpoint;
            ExpiresAt = expiresAt;
        }
    }


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

        public IReadOnlyCollection<IMember> Members => otherMembers;

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
        public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;

        /// <summary>
        /// The protocol period loop (failure detection).
        /// </summary>
        private SwimProtocolPeriodLoop? protocolPeriod;

        public SwimClusterMembership(ILoggerFactory loggerFactory, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer, ITime time)
        {
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger<SwimClusterMembership>();
            this.serializer = serializer;
            this.time = time;
            this.options = options.Value;
            this.otherMembers = new SnapshottedReadOnlyList<SwimMember>();
            this.indirectPingRequests = new SnapshottedReadOnlyList<IndirectPingRequest>();

            this.multicastGroupAddress = IPAddress.Parse(this.options.MulticastGroupAddress);

            // This member status
            this.memberSelf = new SwimMemberSelf(this.options.NodeId, 0);
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

                    logger.LogInformation("Node listening on {NodeEndPoint}", udpClient.Client.LocalEndPoint);
                }

                loopCts = new CancellationTokenSource();

                // Run the message processing loop
                recieveLoopTask = Task.Factory.StartNew(() => RecieveLoop(), TaskCreationOptions.LongRunning);

                protocolPeriod = new SwimProtocolPeriodLoop(loggerFactory.CreateLogger<SwimProtocolPeriodLoop>(), options, this, otherMembers, time);

                isStarted = true;

                logger.LogInformation("Cluster membership protocol (SWIM) started");

                await NotifyJoined();
            }
        }

        public async Task Stop()
        {
            if (isStarted)
            {
                logger.LogInformation("Cluster membership protocol (SWIM) stopping...");

                loopCts?.Cancel();

                if (protocolPeriod != null)
                {
                    await protocolPeriod.DisposeAsync();
                    protocolPeriod = null;
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

                await NotifyLeft();

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
            var payload = serializer.Serialize(message);
            logger.LogTrace("Sending message to {NodeEndPoint}", endPoint);
            return udpClient?.SendAsync(payload, payload.Length, endPoint) ?? Task.CompletedTask;
        }

        protected void NotifyStatusChanged(SwimMember member)
        {
            MemberStatusChanged?.Invoke(this, new MemberEventArgs(member, time.Now));
        }

        protected Task NotifyJoined() =>
            // Announce (multicast) to others that this node joined the network
            SendToMulticastGroup(new NodeMessage { NodeJoined = new NodeJoinedMessage(memberSelf.Id, memberSelf.Incarnation) }, nameof(NodeJoinedMessage));

        protected Task NotifyLeft() =>
            // Announce (multicast) to others that this node left the network
            SendToMulticastGroup(new NodeMessage { NodeLeft = new NodeLeftMessage(memberSelf.Id, memberSelf.Incarnation) }, nameof(NodeLeftMessage));

        private Task SendToMulticastGroup(NodeMessage message, string messageType)
        {
            var endPoint = new IPEndPoint(multicastGroupAddress, options.Port);
            logger.LogInformation("Sending {MessageType} for node {NodeId} (incarnation {NodeIncarnation}) on the multicast group {MulticastEndPoint}", messageType, memberSelf.Id, memberSelf.Incarnation, endPoint);
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
                            await OnMessage(msg, result.RemoteEndPoint);
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

        private async Task OnMessage(NodeMessage msg, IPEndPoint remoteEndPoint)
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
                await OnNodeLeft(msg.NodeLeft, remoteEndPoint);
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
        }

        protected Task OnNodeJoined(NodeJoinedMessage m, IPEndPoint senderEndPoint)
        {
            // Add other members only
            if (m.NodeId != memberSelf.Id)
            {
                logger.LogInformation("Node {NodeId} (incarnation {NodeIncarnation}) joined at {NodeEndPoint}", m.NodeId, m.Incarnation, senderEndPoint);

                var member = new SwimMember(m.NodeId, new IPEndPointAddress(senderEndPoint), time.Now, m.Incarnation, SwimMemberStatus.Active, NotifyStatusChanged);
                otherMembers.Mutate(list => list.Add(member));

                try
                {
                    MemberJoined?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberJoined));
                }

                // ToDo: Perhaps toss coin if this should be provided to avoid every node sending the same memberlist data?
                // Send welcome
                _ = SendWelcome(m, senderEndPoint);
            }
            return Task.CompletedTask;
        }

        private Task SendWelcome(NodeJoinedMessage m, IPEndPoint endPoint)
        {
            var message = new NodeMessage
            {
                NodeWelcome = new NodeWelcomeMessage
                {
                    Nodes = Members
                        .Where(x => x.Node.Id != m.NodeId)
                        .Select(x => new ActiveNode
                        {
                            NodeId = x.Node.Id,
                            NodeAddress = ((IPEndPointAddress)x.Node.Address).EndPoint.ToString(),
                            NodePort = ((IPEndPointAddress)x.Node.Address).EndPoint.Port
                        })
                        .ToList()
                }
            };

            if (message.NodeWelcome.Nodes.Count == 0)
            {
                // Nothing to share with the new joiner
                return Task.CompletedTask;
            }

            logger.LogDebug("Sending Welcome to {NodeId} on {NodeEndPoint} with {NodeCount} known members", m.NodeId, endPoint, message.NodeWelcome.Nodes.Count);

            return SendMessage(message, endPoint);
        }

        protected Task OnNodeWelcome(NodeWelcomeMessage m, IPEndPoint endPoint)
        {
            logger.LogInformation("Recieved starting list of nodes ({NodeCount}) from {NodeEndPoint}", m.Nodes.Count, endPoint);

            otherMembers.Mutate(list =>
            {
                foreach (var node in m.Nodes)
                {
                    if (list.All(x => x.Id != node.NodeId))
                    {
                        var member = new SwimMember(node.NodeId, new IPEndPointAddress(endPoint), time.Now, -1 /* ToDo: Do we need to pass incarnation? */, SwimMemberStatus.Active, NotifyStatusChanged);
                        list.Add(member);
                    }
                }
            });

            // ToDo: Invoke an event hook

            return Task.CompletedTask;
        }

        protected Task OnNodeLeft(NodeLeftMessage m, IPEndPoint endPoint)
        {
            // Add other members only
            if (m.NodeId != memberSelf.Id)
            {
                logger.LogInformation("Node {NodeId} (incarnation {NodeIncarnation}) left at {NodeEndPoint}", m.NodeId, m.Incarnation, endPoint);

                var member = otherMembers.SingleOrDefault(x => x.Node.Id == m.NodeId);
                if (member != null)
                {
                    otherMembers.Mutate(list => list.Remove(member));

                    try
                    {
                        MemberLeft?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberLeft));
                    }
                }
            }
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
            var targetNodeEndpoint = new IPEndPoint(IPAddress.Parse(m.NodeAddress), m.NodePort);

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
            if (protocolPeriod == null)
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
            await protocolPeriod.OnPingAck(m, senderEndPoint);
        }
    }
}
