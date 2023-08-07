namespace SlimCluster.Transport.Ip.Test;

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

public class TestSocketClient : ISocketClient
{
    private readonly Channel<(IPEndPoint, byte[])> _channel;
    private readonly ConcurrentBag<(IPEndPoint, byte[])> _messagesSent = new();
    private readonly List<Func<IPEndPoint, byte[], Task>> _messageSentCallbacks = new();

    public TestSocketClient()
    {
        _channel = Channel.CreateUnbounded<(IPEndPoint, byte[])>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void DropMulticastGroup(IPAddress multicastAddress) { }

    public void JoinMulticastGroup(IPAddress multicastAddress) { }

    public async Task<(IPEndPoint RemoteEndPoint, byte[] Payload)> ReceiveAsync(CancellationToken cancellationToken)
        => await _channel.Reader.ReadAsync(cancellationToken);

    public async Task SendAsync(IPEndPoint endPoint, byte[] payload)
    {
        _messagesSent.Add((endPoint, payload));
        foreach (var callback in _messageSentCallbacks)
        {
            await callback(endPoint, payload);
        }
    }

    public async Task OnMessageArrived(IPEndPoint endPoint, byte[] payload)
        => await _channel.Writer.WriteAsync((endPoint, payload));

    public void OnMessageSend(Func<IPEndPoint, byte[], Task> callback)
        => _messageSentCallbacks.Add(callback);
}
