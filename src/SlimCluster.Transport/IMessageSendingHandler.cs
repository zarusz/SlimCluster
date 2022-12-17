namespace SlimCluster.Transport;

public interface IMessageSendingHandler : IMessageHandler
{
    Task OnMessageSending(object message, IAddress address);
}
