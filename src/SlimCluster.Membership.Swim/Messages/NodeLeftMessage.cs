namespace SlimCluster.Membership.Swim.Messages;

public class NodeLeftMessage : SwimMessage
{
    protected NodeLeftMessage()
    {
    }
    
    public NodeLeftMessage(string fromNodeId) => FromNodeId = fromNodeId;
}
