namespace SlimCluster.Membership.Swim.Messages;

public class AckMessage : SwimMessage, IHasNodeId, IHasPeriodSequenceNumber, IHasMembershipEvents
{
    public string NodeId { get; set; } = string.Empty;
    public long PeriodSequenceNumber { get; set; }
    /// <inheritdoc/>
    public IEnumerable<MembershipEvent>? Events { get; set; }

    protected AckMessage()
    {
    }

    public AckMessage(string fromNodeId) : base(fromNodeId)
    {
    }
}
