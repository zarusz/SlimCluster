namespace SlimCluster.Membership.Swim;

using Microsoft.Extensions.Logging;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Serialization;

public class MessageEndpoint : IMessageSender, IAsyncDisposable
{
    private readonly ILogger<MessageEndpoint> _logger;
    private readonly SwimClusterMembershipOptions _options;
    private readonly ISerializer _serializer;
    private readonly Action<NodeMessage, IPEndPoint> _onMessageSending;
    private readonly Func<NodeMessage, IPEndPoint, Task> _onMessageArrived;

    private readonly IPAddress _multicastGroupAddress;

    private readonly object _udpClientLock = new();
    private UdpClient? _udpClient;

    private CancellationTokenSource? _recieveLoopCts;
    private Task? _recieveLoopTask;

    public IPAddress MulticastGroupAddress => _multicastGroupAddress;

    public MessageEndpoint(ILogger<MessageEndpoint> logger, SwimClusterMembershipOptions options, ISerializer serializer, Action<NodeMessage, IPEndPoint> onMessageSending, Func<NodeMessage, IPEndPoint, Task> onMessageArrived)
    {
        _logger = logger;
        _options = options;
        _serializer = serializer;
        _onMessageSending = onMessageSending;
        _onMessageArrived = onMessageArrived;

        _multicastGroupAddress = IPAddress.Parse(_options.MulticastGroupAddress);

        //NetworkInterface.GetAllNetworkInterfaces()
        //var ip = IPAddress.Parse("192.168.100.50");
        //var ep = new IPEndPoint(ip, options.UdpPort);
        //udpClient = new UdpClient(ep);
        // Join or create a multicast group
        _udpClient = new UdpClient(_options.Port, _options.AddressFamily);
        // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0
        _udpClient.JoinMulticastGroup(_multicastGroupAddress);

        _recieveLoopCts = new CancellationTokenSource();
        // Run the message processing loop
        _recieveLoopTask = Task.Factory.StartNew(RecieveLoop, TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync()
    {
        _recieveLoopCts?.Cancel();

        if (_recieveLoopTask != null)
        {
            try
            {
                await _recieveLoopTask;
            }
            catch
            {
            }
            _recieveLoopTask = null;
        }

        if (_recieveLoopCts != null)
        {
            _recieveLoopCts.Dispose();
            _recieveLoopCts = null;
        }

        // Stop multicast group
        lock (_udpClientLock)
        {
            if (_udpClient != null)
            {
                _udpClient.DropMulticastGroup(_multicastGroupAddress);
                _udpClient.Dispose();
                _udpClient = null;
            }
        }
    }

    public Task SendMessage<T>(T message, IPEndPoint endPoint) where T : class
    {
        if (message is NodeMessage nodeMessage)
        {
            _onMessageSending(nodeMessage, endPoint);
        }

        _logger.LogTrace("Sending message {Message} to {NodeEndPoint}", message, endPoint);
        var payload = _serializer.Serialize(message);
        return _udpClient?.SendAsync(payload, payload.Length, endPoint) ?? Task.CompletedTask;
    }

    private async Task RecieveLoop()
    {
        _logger.LogInformation("Recieve loop started. Node listening on {NodeEndPoint}", _udpClient!.Client.LocalEndPoint);
        try
        {
            while (_recieveLoopCts == null || !_recieveLoopCts.IsCancellationRequested)
            {
                var result = await _udpClient!.ReceiveAsync();
                try
                {
                    var msg = _serializer.Deserialize<NodeMessage>(result.Buffer);
                    if (msg != null)
                    {
                        await _onMessageArrived(msg, result.RemoteEndPoint);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Could not handle arriving message from remote endpoint {RemoteEndPoint}", result.RemoteEndPoint);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Intended: this is how it exists from ReceiveAsync
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Recieve loop error");
        }
        finally
        {
            _logger.LogInformation("Recieve loop finished");
        }
    }
}
