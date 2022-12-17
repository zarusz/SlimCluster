namespace SlimCluster.Transport.Ip;

public class IPEndPointAddress : IAddress
{
    public IPEndPoint EndPoint { get; }

    public IPEndPointAddress(IPEndPoint endPoint) => EndPoint = endPoint;

    public override string ToString() => EndPoint.ToString();

    public override bool Equals(object? obj) => obj is IPEndPointAddress address && EqualityComparer<IPEndPoint>.Default.Equals(EndPoint, address.EndPoint);
    public override int GetHashCode() => HashCode.Combine(EndPoint);

    public static readonly IPEndPointAddress Unknown = new(new IPEndPoint(IPAddress.None, 0));

    IAddress IAddress.Parse(string s) => Parse(s);

    public static IPEndPointAddress Parse(string s)
    {
        var elements = s.Split(":", StringSplitOptions.RemoveEmptyEntries);
        if (elements.Length != 2)
        {
            throw new ApplicationException($"Invalid node address format: {s}");
        }

        var ipEndPoint = new IPEndPoint(IPAddress.Parse(elements[0]), int.Parse(elements[1]));
        return new IPEndPointAddress(ipEndPoint);
    }

    public static IPEndPointAddress From(IPEndPoint endPoint) => new(endPoint);

    public bool Equals(IAddress other) => Equals((object?)other);
}

public static class AddressExtensions
{
    public static IPEndPoint ToIPEndPoint(this IAddress address) => ((IPEndPointAddress)address).EndPoint;
}
