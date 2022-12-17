namespace SlimCluster.Membership.Swim.Messages;

public class PingMessage : SwimMessage, IHasPeriodSequenceNumber, IHasMembershipEvents
{
    public long PeriodSequenceNumber { get; set; }
    /// <inheritdoc/>
    public IEnumerable<MembershipEvent>? Events { get; set; }

    protected PingMessage()
    {
    }

    public PingMessage(string fromNodeId) : base(fromNodeId)
    {
    }
}
