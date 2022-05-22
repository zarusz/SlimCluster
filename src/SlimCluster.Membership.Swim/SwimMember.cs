namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using System;

    public class SwimMember : IMember, INode
    {
        private readonly ILogger<SwimMember> logger;
        private readonly Action<SwimMember>? notifyStatusChanged;

        public string Id { get; }
        public int Incarnation { get; }
        IAddress INode.Address => Address;
        public INodeStatus Status => SwimStatus;

        public SwimMemberStatus SwimStatus { get; protected set; }

        /// <summary>
        /// Point in time after which the Suspicious node will be declared as Confirm if no ACK is recieved.
        /// </summary>
        public DateTimeOffset? SuspiciousTimeout { get; set; }
        public DateTimeOffset? LastPing { get; set; }

        #region IMember

        public INode Node => this;
        public DateTimeOffset Joined { get; protected set; }
        public DateTimeOffset LastSeen { get; protected set; }

        #endregion

        public IPEndPointAddress Address { get; protected set; }

        public SwimMember(string id, IPEndPointAddress address, DateTimeOffset joined, int incarnation, SwimMemberStatus status, Action<SwimMember>? notifyStatusChanged, ILogger<SwimMember> logger)
        {
            this.logger = logger;
            this.notifyStatusChanged = notifyStatusChanged;

            Id = id;
            Incarnation = incarnation;
            Address = address;
            SwimStatus = status;
            Joined = joined;
            LastSeen = joined;
        }

        public override string ToString() => $"{Id}/({Address})";

        public void OnActive(ITime time)
        {
            if (Status == SwimMemberStatus.Confirming || Status == SwimMemberStatus.Suspicious)
            {
                SuspiciousTimeout = null;

                LastSeen = time.Now;
                ChangeStatusTo(SwimMemberStatus.Active);
            }
        }

        private void ChangeStatusTo(SwimMemberStatus newStatus)
        {
            // When substantial transistion from active non-active or vice versa log with Info, otherwise Debug
            var logLevel = newStatus.IsActive != SwimStatus.IsActive ? LogLevel.Information : LogLevel.Debug;
            logger.Log(logLevel, "Member {NodeId} changes status to {NodeStatus} (previous {PreviousNodeStatus})", Id, newStatus, Status);
            SwimStatus = newStatus;
            notifyStatusChanged?.Invoke(this);
        }

        public void OnConfirming(DateTimeOffset periodTimeout)
        {
            if (Status == SwimMemberStatus.Active || Status == SwimMemberStatus.Suspicious)
            {
                SuspiciousTimeout = periodTimeout;

                ChangeStatusTo(SwimMemberStatus.Confirming);
            }
        }

        public void OnSuspicious()
        {
            if (Status == SwimMemberStatus.Confirming)
            {
                ChangeStatusTo(SwimMemberStatus.Suspicious);
            }
        }
    }
}
