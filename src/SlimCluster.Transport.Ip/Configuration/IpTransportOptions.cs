namespace SlimCluster.Transport.Ip;

using System.Net.Sockets;

public class IpTransportOptions
{
    /// <summary>
    /// UDP port used for internal membership message exchange
    /// </summary>
    public int Port { get; set; } = 60001;

    // ToDo: Interface index
    // ToDo: IP to bind to
    public AddressFamily AddressFamily { get; set; } = AddressFamily.InterNetwork;

    /// <summary>
    /// UDP multicast group for cluster announcements. If Null, multicast won't be used.
    /// </summary>
    public string MulticastGroupAddress { get; set; } = "239.1.1.1"; //"FF01::1";

    /// <summary>
    /// Default request timeout (when the response doesn't arrive withing this time the request is cancelled).
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
