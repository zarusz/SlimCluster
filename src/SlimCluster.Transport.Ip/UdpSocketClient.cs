namespace SlimCluster.Transport.Ip;

using System.Net.Sockets;

public class UdpSocketClient : ISocketClient
{
    private readonly UdpClient _udpClient;

    public UdpSocketClient(IpTransportOptions options)
    {
        _udpClient = new UdpClient(options.Port, options.AddressFamily);
    }

    public void JoinMulticastGroup(IPAddress multicastAddress) => _udpClient.JoinMulticastGroup(multicastAddress);
    public void DropMulticastGroup(IPAddress multicastAddress) => _udpClient.DropMulticastGroup(multicastAddress);
    public Task SendAsync(IPEndPoint endPoint, byte[] payload) => _udpClient.SendAsync(payload, payload.Length, endPoint);
    public async Task<(IPEndPoint RemoteEndPoint, byte[] Payload)> ReceiveAsync(CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        var result = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
#else        
        var receiveTask = _udpClient.ReceiveAsync();
        while (!receiveTask.IsCompleted)
        {
            await Task.WhenAny(receiveTask, Task.Delay(100, cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();
        }
        var result = receiveTask.Result;
#endif
        return (result.RemoteEndPoint, result.Buffer);
    }

    public void Dispose()
    {
        _udpClient.Close();
        _udpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
