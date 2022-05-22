namespace SlimCluster.Membership.Swim
{
    using SlimCluster.Membership.Swim.Messages;
    using System;
    using System.Net;

    public class IndirectPingRequest : IHasPeriodSequenceNumber
    {
        public long PeriodSequenceNumber { get; set; }
        public IPEndPoint RequestingEndpoint { get; private set; }
        public IPEndPoint TargetEndpoint { get; private set; }
        /// <summary>
        /// Time after which the request is no longer needed
        /// </summary>
        public DateTimeOffset ExpiresAt { get; private set; }

        public IndirectPingRequest(long periodSequenceNumber, IPEndPoint requestingEndpoint, IPEndPoint targetEndpoint, DateTimeOffset expiresAt)
        {
            PeriodSequenceNumber = periodSequenceNumber;
            RequestingEndpoint = requestingEndpoint;
            TargetEndpoint = targetEndpoint;
            ExpiresAt = expiresAt;
        }
    }
}
