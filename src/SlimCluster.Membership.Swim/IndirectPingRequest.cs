namespace SlimCluster.Membership.Swim;

using SlimCluster.Membership.Swim.Messages;

public class IndirectPingRequest : IHasPeriodSequenceNumber
{
    public long PeriodSequenceNumber { get; set; }
    public IAddress RequestingEndpoint { get; private set; }
    public IAddress TargetEndpoint { get; private set; }
    /// <summary>
    /// Time after which the request is no longer needed
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public IndirectPingRequest(long periodSequenceNumber, IAddress requestingAddress, IAddress targetAddress, DateTimeOffset expiresAt)
    {
        PeriodSequenceNumber = periodSequenceNumber;
        RequestingEndpoint = requestingAddress;
        TargetEndpoint = targetAddress;
        ExpiresAt = expiresAt;
    }
}
