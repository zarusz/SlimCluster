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

        /// <summary>
        /// Other known members
        /// </summary>
        private readonly SnapshottedReadOnlyList<SwimMember> otherMembers;

        /// <summary>
        /// Member representing current node.
        /// </summary>
        private readonly SwimMemberSelf memberSelf;

        public string ClusterId => options.ClusterId;

        public IReadOnlyCollection<IMember> Members => otherMembers;

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
        public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;

        /// <summary>
        /// The protocol period loop (failure detection).
        /// </summary>
        private SwimProtocolPeriodLoop? protocolPeriod;

        public SwimClusterMembership(ILoggerFactory loggerFactory, IOptions<SwimClusterMembershipOptions> options, ISerializer serializer)
        {
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger<SwimClusterMembership>();
            this.serializer = serializer;
            this.options = options.Value;
            this.otherMembers = new SnapshottedReadOnlyList<SwimMember>();

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

                protocolPeriod = new SwimProtocolPeriodLoop(loggerFactory.CreateLogger<SwimProtocolPeriodLoop>(), options, this, otherMembers);

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
            MemberStatusChanged?.Invoke(this, new MemberEventArgs(member, DateTimeOffset.Now));
        }

        protected Task NotifyJoined()
        {
            // Announce (multicast) to others that this node joined the network

            var message = new NodeMessage
            {
                NodeJoined = new NodeJoinedMessage(memberSelf.Id, memberSelf.Incarnation)
            };

            var endPoint = new IPEndPoint(multicastGroupAddress, options.Port);

            logger.LogInformation("Sending Joined message for node {NodeId} (incarnation {NodeIncarnation}) on the multicast group {MulticastEndPoint}", message.NodeJoined.NodeId, message.NodeJoined.Incarnation, endPoint);

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

        protected Task OnNodeJoined(NodeJoinedMessage m, IPEndPoint endPoint)
        {
            logger.LogInformation("Node {NodeId} (incarnation {NodeIncarnation}) joined at {NodeEndPoint}", m.NodeId, m.Incarnation, endPoint);

            // Add other members only
            if (m.NodeId != memberSelf.Id)
            {
                var member = new SwimMember(m.NodeId, new IPEndPointAddress(endPoint), DateTime.UtcNow, m.Incarnation, SwimMemberStatus.Active, NotifyStatusChanged);
                otherMembers.Mutate(list => list.Add(member));

                try
                {
                    MemberJoined?.Invoke(this, new MemberEventArgs(member.Node, member.LastSeen));
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Exception while invoking event {HandlerName}", nameof(MemberJoined));
                }
            }

            return Task.CompletedTask;
        }

        protected Task OnPing(PingMessage m, IPEndPoint endPoint)
        {
            var responseMessage = new NodeMessage
            {
                Ack = new AckMessage
                {
                    NodeId = memberSelf.Id,
                    PeriodSequenceNumber = m.PeriodSequenceNumber,
                }
            };

            logger.LogDebug("Sending Ack with node {NodeId}, period sequence number {PeriodSequenceNumber} to remote {NodeEndPoint}", responseMessage.Ack.NodeId, responseMessage.Ack.PeriodSequenceNumber, endPoint);

            return SendMessage(responseMessage, endPoint);
        }

        protected Task OnPingReq(PingReqMessage m, IPEndPoint endPoint)
        {
            return Task.CompletedTask;
        }

        protected Task OnPingAck(AckMessage m, IPEndPoint endPoint)
        {
            if (protocolPeriod == null)
            {
                // This is stopping (disposing).
                return Task.CompletedTask;
            }

            if (protocolPeriod.PeriodSequenceNumber != m.PeriodSequenceNumber)
            {
                logger.LogDebug("Ack arrived too late for the node {NodeId}, period {PeriodSequenceNumber}, while the Ack message was for period {AckPeriodSequenceNumber}", m.NodeId, protocolPeriod.PeriodSequenceNumber, m.PeriodSequenceNumber);
                return Task.CompletedTask;
            }

            var node = otherMembers.SingleOrDefault(x => x.Id == m.NodeId);
            if (node != null)
            {
                node.OnActive();

                logger.LogDebug("Ack arrived for the node {NodeId}, period {PeriodSequenceNumber}, node status {NodeStatus}", m.NodeId, protocolPeriod.PeriodSequenceNumber, node.Status);
            }

            return Task.CompletedTask;
        }
    }
}
