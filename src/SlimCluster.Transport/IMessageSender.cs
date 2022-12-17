namespace SlimCluster.Transport;

// ToDo: Move this interface to use the IAddress abstraction
// ToDo: Extract into SlimCluster.Transport layer
public interface IMessageSender
{
    /// <summary>
    /// Sends the specified message to the specified UDP endpoint.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="endPoint"></param>
    /// <returns></returns>
    Task SendMessage(object message, IAddress address);

    /// <summary>
    /// Sends the specified request message to the specified UDP endpoint and awaits a response.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="endPoint"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, IAddress address, TimeSpan? timeout = null)
        where TResponse : class, IResponse;

    IAddress? MulticastGroupEndpoint { get; }
}
