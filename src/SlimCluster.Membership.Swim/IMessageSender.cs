namespace SlimCluster.Membership.Swim;

public interface IMessageSender
{
    /// <summary>
    /// Sends the specified message to the specified UDP endpoint.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="endPoint"></param>
    /// <returns></returns>
    Task SendMessage<T>(T message, IPEndPoint endPoint) where T : class;
}
