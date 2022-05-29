namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using System.Net;

    public class SwimMemberSelf : SwimMember
    {
        private ILogger<SwimMemberSelf> logger;

        public SwimMemberSelf(string id, IPEndPointAddress address, ITime time, ILoggerFactory loggerFactory)
            : base(id, address, time.Now, SwimMemberStatus.Active, notifyStatusChanged: null, loggerFactory.CreateLogger<SwimMember>())
        {
            this.logger = loggerFactory.CreateLogger<SwimMemberSelf>();
        }

        public bool OnObservedAddress(IPEndPoint endpoint)
        {
            if (!endpoint.Equals(Address.EndPoint))
            {
                // Record the observed external IP address for self
                Address = new IPEndPointAddress(endpoint);

                logger.LogInformation("Updated observed address of self to {NodeEndPoint}", Address);

                return true;
            }
            return false;
        }
    }
}
