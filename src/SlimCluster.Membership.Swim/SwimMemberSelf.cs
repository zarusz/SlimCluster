namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.Logging;
    using System.Net;

    public class SwimMemberSelf : SwimMember
    {
        private ILogger<SwimMemberSelf> logger;

        public SwimMemberSelf(string id, int incarnation, IPEndPointAddress address, ITime time, ILogger<SwimMemberSelf> logger)
            : base(id, address, time.Now, incarnation, SwimMemberStatus.Active, notifyStatusChanged: null)
        {
            this.logger = logger;
        }

        public bool OnObservedAddress(IPEndPoint endpoint)
        {
            if (endpoint != Address.EndPoint)
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
