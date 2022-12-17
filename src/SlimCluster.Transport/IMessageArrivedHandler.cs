namespace SlimCluster.Transport;

public interface IMessageArrivedHandler : IMessageHandler
{    
    Task OnMessageArrived(object message, IAddress address);
}
