﻿namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using SlimCluster.Membership.Swim.Messages;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class SwimProtocolPeriodLoop : IAsyncDisposable
    {
        private readonly ILogger<SwimProtocolPeriodLoop> logger;
        private readonly SwimClusterMembershipOptions options;
        private readonly IClusterMessageSender messageSender;
        private readonly IReadOnlyList<SwimMember> otherMembers;
        private readonly Random random = new();

        /// <summary>
        /// The current protocol period sequence number.
        /// </summary>
        private long periodSequenceNumber;

        /// <summary>
        /// The time that the current protocol period ends.
        /// </summary>
        private DateTimeOffset periodTimeout;

        /// <summary>
        /// Timer that performs the checks for the expected timeouts.
        /// </summary>
        private Timer? periodTimer;

        private SwimMember? pingNode;
        private DateTimeOffset? pingAckTimeout;

        public long PeriodSequenceNumber => Interlocked.Read(ref periodSequenceNumber);

        private bool timerMethodRunning;

        public SwimProtocolPeriodLoop(
            ILogger<SwimProtocolPeriodLoop> logger, 
            SwimClusterMembershipOptions options, 
            IClusterMessageSender messageSender, 
            IReadOnlyList<SwimMember> otherMembers)
        {
            this.logger = logger;
            this.options = options;
            this.messageSender = messageSender;
            this.otherMembers = otherMembers;
            AdvancePeriod(DateTimeOffset.Now);
            periodTimer = new Timer(async (o) => await OnTimer(), null, 0, (int)options.PeriodTimerInterval.TotalMilliseconds);
        }

        public async ValueTask DisposeAsync()
        {
            if (periodTimer != null)
            {
                periodTimer.Change(Timeout.Infinite, Timeout.Infinite);

                await periodTimer.DisposeAsync();
                periodTimer = null;
            }
        }

        public async Task OnTimer()
        {
            if (timerMethodRunning)
            {
                // another run is happening at the moment - prevent from rentering
                return;
            }

            timerMethodRunning = true;
            try
            {
                await DoRun();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error occured at protocol period timer loop");
            }
            finally
            {
                timerMethodRunning = false;
            }
        }

        private async Task DoRun()
        {
            var now = DateTimeOffset.Now;
            if (now >= periodTimeout)
            {
                // Start a new period
                await OnNewPeriod();
            }
            else
            {
                if (pingAckTimeout != null && now >= pingAckTimeout.Value)
                {
                    pingAckTimeout = null;
                    await OnPingTimeout();
                }
            }
        }

        private void AdvancePeriod(DateTimeOffset now)
        {
            periodTimeout = now.Add(options.ProtocolPeriod);
            Interlocked.Increment(ref periodSequenceNumber);

            logger.LogDebug("Started period {PeriodSequenceNumber} and timeout on {PeriodTimeout}", PeriodSequenceNumber, periodTimeout);
        }

        private Task OnPingTimeout()
        {
            // get the active members only
            var activeMembers = otherMembers.Where(x => x.Status == SwimMemberStatus.Active).ToList();
            if (activeMembers.Count == 0)
            {
                // no active members
                return Task.CompletedTask;
            }

            // choose a subgroup of active members
            var selectedMembers = new List<SwimMember>(options.FailureDetectionSubgroupSize);
            for (var i = options.FailureDetectionSubgroupSize; i > 0 && activeMembers.Count > 0; i--)
            {
                var selectedMemberIndex = random.Next(activeMembers.Count);
                var selectedMember = activeMembers[selectedMemberIndex];

                activeMembers.RemoveAt(selectedMemberIndex);
            }

            if (pingNode == null)
            {
                // in case this would have changed
                return Task.CompletedTask;
            }

            var targetNodeAddress = pingNode.Address.EndPoint.Address.ToString();
            var targetNodePort = pingNode.Address.EndPoint.Port;

            Task SendPingReq(SwimMember member)
            {
                var message = new NodeMessage
                {
                    PingReq = new PingReqMessage
                    {
                        PeriodSequenceNumber = PeriodSequenceNumber,
                        TargetNodeAddress = targetNodeAddress,
                        TargetNodePort = targetNodePort,
                    }
                };
                return messageSender.SendMessage(message, member.Address.EndPoint);
            }

            // send the ping-req to them (in pararell)
            return Task.WhenAll(selectedMembers.Select(m => SendPingReq(m)));
        }

        private async Task OnNewPeriod()
        {
            var now = DateTimeOffset.Now;

            // Declare node that did not recieve an Ack for the Ping as Failed
            if (pingNode != null)
            {
                if (pingNode.Status == SwimMemberStatus.Confirming)
                {
                    // When the node Ack did not arrive (via direct ping or via inderect ping-req) then declare this node as unhealty
                    pingNode.OnSuspicious();

                    logger.LogInformation("Node {NodeId} was declared as {NodeStatus} - ack message did not arrive in time for period {PeriodSequenceNumber}", pingNode.Id, pingNode.Status, PeriodSequenceNumber);
                }
            }

            AdvancePeriod(now);

            await SelectMemberForPing(now);
        }

        private async Task SelectMemberForPing(DateTimeOffset now)
        {
            if (otherMembers.Count == 0)
            {
                // nothing to available to select
                pingNode = null;
                pingAckTimeout = null;
                return;
            }

            // select random node for ping
            var i = random.Next(otherMembers.Count);

            // store the selected node ID
            pingNode = otherMembers[i];
            // expect an ack after the specified timeout
            pingAckTimeout = now.Add(options.PingAckTimeout);

            pingNode.OnConfirming(periodTimeout);

            logger.LogDebug("Node {NodeId} was selected for failure detecton at period {PeriodSequenceNumber} - ping message will be sent", pingNode.Id, PeriodSequenceNumber);

            var message = new NodeMessage
            {
                Ping = new PingMessage
                {
                    PeriodSequenceNumber = periodSequenceNumber,
                }
            };
            await messageSender.SendMessage(message, pingNode.Address.EndPoint);
        }
    }
}