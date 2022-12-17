namespace SlimCluster.Transport;

public interface IMessageHandler
{
    bool CanHandle(object message);
}
