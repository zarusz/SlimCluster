namespace SlimCluster.Membership.Swim.Messages;

public class NodeLeftMessage : SwimMessage
{
    public long Incarnation { get; set; }

    protected NodeLeftMessage()
    {
    }

    public NodeLeftMessage(string fromNodeId, long incarnation = 0)
        : base(fromNodeId)
    {
        Incarnation = incarnation;
    }
}
