namespace SlimCluster.Membership.Swim.Messages
{
    public class PingMessage : IHasPeriodSequenceNumber
    {
        public long PeriodSequenceNumber { get; set; }
    }
}
