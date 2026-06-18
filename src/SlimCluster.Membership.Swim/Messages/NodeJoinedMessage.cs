namespace SlimCluster.Membership.Swim.Messages;

public class NodeJoinedMessage : SwimMessage
{
    public long Incarnation { get; set; }

    protected NodeJoinedMessage()
    {
    }

    public NodeJoinedMessage(string fromNodeId, long incarnation = 0)
        : base(fromNodeId)
    {
        Incarnation = incarnation;
    }
}
