namespace SlimCluster.Membership.Swim.Messages;

public class NodeJoinedMessage : SwimMessage
{
    protected NodeJoinedMessage()
    {
    }

    public NodeJoinedMessage(string fromNodeId) => FromNodeId = fromNodeId;
}
