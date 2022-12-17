namespace SlimCluster.Transport.Ip;

public class IPMessageSender : IMessageSender
{
    private readonly Lazy<IPMessageEndpoint> _messageEndpoint;

    public IPMessageSender(Lazy<IPMessageEndpoint> messageEndpoint) => _messageEndpoint = messageEndpoint;

    public IAddress? MulticastGroupEndpoint => _messageEndpoint.Value.MulticastGroupEndpoint;

    public Task SendMessage(object message, IAddress address) => _messageEndpoint.Value.SendMessage(message, address);
    
    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, IAddress address, TimeSpan? timeout)
        where TResponse : class, IResponse
        => _messageEndpoint.Value.SendRequest(request, address, timeout);
}
