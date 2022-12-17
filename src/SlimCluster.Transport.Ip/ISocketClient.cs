namespace SlimCluster.Transport.Ip;

public interface ISocketClient : IDisposable
{
    void JoinMulticastGroup(IPAddress multicastAddress);
    void DropMulticastGroup(IPAddress multicastAddress);
    Task SendAsync(IPEndPoint endPoint, byte[] payload);
    Task<(IPEndPoint RemoteEndPoint, byte[] Payload)> ReceiveAsync();
}
