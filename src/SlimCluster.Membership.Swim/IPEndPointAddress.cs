namespace SlimCluster.Membership.Swim
{
    using System.Net;

    public class IPEndPointAddress : IAddress
    {
        public IPEndPoint EndPoint { get; }

        public IPEndPointAddress(IPEndPoint endPoint) => EndPoint = endPoint;

        public override string ToString() => EndPoint.ToString();
    }
}
