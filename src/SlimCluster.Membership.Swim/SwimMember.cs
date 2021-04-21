namespace SlimCluster.Membership.Swim
{
    using System;

    public class SwimMember : IMember, INode
    {
        private readonly Action<SwimMember> notifyStatusChanged;

        public string Id { get; }
        public int Incarnation { get; }
        IAddress INode.Address => Address;
        public INodeStatus Status { get; protected set; }
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

        public SwimMember(string id, IPEndPointAddress address, DateTimeOffset joined, int incarnation, SwimMemberStatus status, Action<SwimMember> notifyStatusChanged)
        {
            Id = id;
            Incarnation = incarnation;
            Address = address;
            Status = status;
            this.notifyStatusChanged = notifyStatusChanged;
            Joined = joined;
            LastSeen = joined;
        }

        public override string ToString() => $"{Id}/({Address})";

        public void OnActive()
        {
            if (Status == SwimMemberStatus.Confirming || Status == SwimMemberStatus.Suspicious)
            {
                SuspiciousTimeout = null;

                LastSeen = DateTimeOffset.Now;
                Status = SwimMemberStatus.Active;
                notifyStatusChanged(this);
            }
        }

        public void OnConfirming(DateTimeOffset periodTimeout)
        {
            if (Status == SwimMemberStatus.Active || Status == SwimMemberStatus.Suspicious)
            {
                SuspiciousTimeout = periodTimeout;
                Status = SwimMemberStatus.Confirming;
                notifyStatusChanged(this);
            }
        }

        public void OnSuspicious()
        {
            if (Status == SwimMemberStatus.Confirming)
            {
                Status = SwimMemberStatus.Suspicious;
                notifyStatusChanged(this);
            }
        }
    }
}
