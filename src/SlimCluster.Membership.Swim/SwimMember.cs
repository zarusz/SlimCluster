namespace SlimCluster.Membership.Swim;

public class SwimMember : AbstractNode, IMember, INode
{
    private readonly ILogger<SwimMember> _logger;
    private readonly Action<SwimMember>? _notifyStatusChanged;

    #region INode

    public override INodeStatus Status => SwimStatus;
    public override IAddress Address { get; protected set; }

    #endregion

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

    public SwimMember(string id, IAddress address, DateTimeOffset joined, SwimMemberStatus status, Action<SwimMember>? notifyStatusChanged, ILogger<SwimMember> logger)
        : base(id)
    {
        _logger = logger;
        _notifyStatusChanged = notifyStatusChanged;

        Address = address;
        SwimStatus = status;
        Joined = joined;
        LastSeen = joined;
    }

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
        _logger.Log(logLevel, "Member {NodeId} changes status to {NodeStatus} (from {NodeStatus})", Id, newStatus, Status);
        SwimStatus = newStatus;
        _notifyStatusChanged?.Invoke(this);
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

    public void OnFaulted()
    {
        if (Status == SwimMemberStatus.Confirming)
        {
            ChangeStatusTo(SwimMemberStatus.Faulted);
        }
    }

    public bool OnObservedAddress(IAddress address)
    {
        if (!Address.Equals(address))
        {
            // Record the observed external IP address for self
            Address = address;

            _logger.LogInformation("Updated member {NodeId} observed address to {NodeAddress}", Id, Address);

            return true;
        }
        return false;
    }
}
