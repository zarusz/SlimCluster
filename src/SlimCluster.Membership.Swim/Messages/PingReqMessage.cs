namespace SlimCluster.Membership.Swim.Messages;
public class PingReqMessage : PingMessage, IHasNodeAddress, IHasNodeId
{
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Node address that sent this Ping
    /// </summary>
    public string NodeAddress { get; set; } = string.Empty;
    
    protected PingReqMessage()
    {
    }

    public PingReqMessage(string fromNodeId) : base(fromNodeId)
    {
    }
}
