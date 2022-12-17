namespace SlimCluster.Transport.Ip;

using System.Collections.Concurrent;

using SlimCluster.Host;
using SlimCluster.Host.Common;

public class IPMessageEndpoint : TaskLoop, IMessageSender, IAsyncDisposable, IClusterControlComponent
{
    private readonly ILogger<IPMessageEndpoint> _logger;
    private readonly IpTransportOptions _options;
    private readonly ISerializer _serializer;
    private readonly IEnumerable<IMessageSendingHandler> _messageSendingHandlers;
    private readonly IEnumerable<IMessageArrivedHandler> _messageArrivedHandlers;

    private readonly IPAddress? _multicastGroupAddress;
    private readonly IAddress? _multicaseGroupEndpoint;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<object>> _requests = new ConcurrentDictionary<Guid, TaskCompletionSource<object>>();

    private ISocketClient? _socketClient;

    public IAddress? MulticastGroupEndpoint => _multicaseGroupEndpoint;

    public int RequestCount => _requests.Count;

    public IPMessageEndpoint(
        ILogger<IPMessageEndpoint> logger,
        IOptions<IpTransportOptions> options,
        ISerializer serializer,
        IEnumerable<IMessageSendingHandler> messageSendingHandlers,
        IEnumerable<IMessageArrivedHandler> messageArrivedHandlers,
        ISocketClient? socketClient = null)
        : base(logger)
    {
        _logger = logger;
        _options = options.Value;
        _serializer = serializer;
        _messageSendingHandlers = messageSendingHandlers;
        _messageArrivedHandlers = messageArrivedHandlers;

        _multicastGroupAddress = _options.MulticastGroupAddress != null ? IPAddress.Parse(_options.MulticastGroupAddress) : null;
        _multicaseGroupEndpoint = _multicastGroupAddress != null ? new IPEndPointAddress(new IPEndPoint(_multicastGroupAddress, _options.Port)) : null;

        //NetworkInterface.GetAllNetworkInterfaces()
        //var ip = IPAddress.Parse("192.168.100.50");
        //var ep = new IPEndPoint(ip, options.UdpPort);
        //udpClient = new UdpClient(ep);
        // Join or create a multicast group
        _socketClient = socketClient ?? new UdpSocketClient(_options);
        // See https://docs.microsoft.com/pl-pl/dotnet/api/system.net.sockets.udpclient.joinmulticastgroup?view=net-5.0
        // Join multicast group (if specified)
        if (_multicastGroupAddress != null)
        {
            try
            {
                _logger.LogInformation("Joining multicast group {MulticastGroup}", _multicastGroupAddress);
                _socketClient.JoinMulticastGroup(_multicastGroupAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not join multicast group {MulticastGroup}", _multicastGroupAddress);
            }
        }

        _ = Start();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await Stop().ConfigureAwait(false);

        // Stop UDP client
        if (_socketClient != null)
        {
            var socketClient = _socketClient;
            // prevent SendMessage from being handled
            _socketClient = null;

            if (_multicastGroupAddress != null)
            {
                try
                {
                    _logger.LogInformation("Leaving multicast group {MulticastGroup}", _multicastGroupAddress);
                    socketClient.DropMulticastGroup(_multicastGroupAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not leave multicast group {MulticastGroup}", _multicastGroupAddress);
                }
            }
            socketClient.Dispose();
        }
    }

    public async Task SendMessage(object message, IAddress address)
    {
        _logger.LogTrace("Sending message {Message} of type {MessageType} to {NodeAddress}", message, message.GetType().Name, address);

        foreach (var handler in _messageSendingHandlers)
        {
            if (handler.CanHandle(message))
            {
                try
                {
                    await handler.OnMessageSending(message, address).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Handler {HandlerType} could not handle arriving message from remote endpoint {RemoteAddress}", handler.GetType(), address);
                }
            }
        }

        var payload = _serializer.Serialize(message);

        if (_socketClient != null)
        {
            await _socketClient.SendAsync(address.ToIPEndPoint(), payload).ConfigureAwait(false);
        }
    }

    public async Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, IAddress address, TimeSpan? timeout = null)
        where TResponse : class, IResponse
    {
        var requestId = request.RequestId;
        var requestSource = new TaskCompletionSource<object>();
        _requests[requestId] = requestSource;
        try
        {
            // Send request
            await SendMessage(request, address).ConfigureAwait(false);

            var finishedTask = await Task.WhenAny(requestSource.Task, Task.Delay(timeout ?? _options.RequestTimeout));
            if (finishedTask != requestSource.Task)
            {
                throw new OperationCanceledException();
            }

            var response = await requestSource.Task.ConfigureAwait(false);

            return (TResponse)response;
        }
        finally
        {
            // Remove the pending request from the list
            _requests.TryRemove(requestId, out _);
        }
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        IPEndPoint? remoteEndPoint = null;
        try
        {
            (remoteEndPoint, var messagePayload) = await _socketClient!.ReceiveAsync().ConfigureAwait(false);

            await OnMessage(IPEndPointAddress.From(remoteEndPoint), messagePayload).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Intended: this is how it exists from ReceiveAsync            
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not handle arriving message from remote endpoint {RemoteAddress}", remoteEndPoint);
        }
        return false;
    }

    private async Task OnMessage(IAddress remoteAddress, byte[] messagePayload)
    {
        var message = _serializer.Deserialize(messagePayload);
        if (message != null)
        {
            _logger.LogTrace("{Message} arrived from remote address {RemoteAddress}", message, remoteAddress);

            foreach (var handler in _messageArrivedHandlers)
            {
                if (handler.CanHandle(message))
                {
                    try
                    {
                        await handler.OnMessageArrived(message, remoteAddress).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Handler {HandlerType} could not handle arriving message from remote endpoint {RemoteAddress}", handler.GetType(), remoteAddress);
                    }
                }
            }

            // Handle responses
            if (message is IResponse response)
            {
                if (_requests.TryGetValue(response.RequestId, out var requestSource))
                {
                    requestSource.TrySetResult(message);
                }
            }
        }
    }
}
