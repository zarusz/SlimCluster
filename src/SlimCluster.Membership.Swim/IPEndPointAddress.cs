namespace SlimCluster.Membership.Swim
{
    using System;
    using System.Collections.Generic;
    using System.Net;

    public class IPEndPointAddress : IAddress
    {
        public IPEndPoint EndPoint { get; }

        public IPEndPointAddress(IPEndPoint endPoint) => EndPoint = endPoint;

        public override string ToString() => EndPoint.ToString();

        public override bool Equals(object? obj) => obj is IPEndPointAddress address && EqualityComparer<IPEndPoint>.Default.Equals(EndPoint, address.EndPoint);
        public override int GetHashCode() => HashCode.Combine(EndPoint);
        
        public static readonly IPEndPointAddress Unknown = new(new IPEndPoint(IPAddress.None, 0));
    }
}
