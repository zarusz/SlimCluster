namespace SlimCluster.Membership.Swim.Messages
{
    public class AckMessage : IHasNodeId, IHasPeriodSequenceNumber
    {
        public long PeriodSequenceNumber { get; set; }

        public string NodeId { get; set; } = string.Empty;
    }
}
