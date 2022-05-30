namespace SlimCluster.Membership.Swim.Messages;

public class NodeJoinedMessage : IHasNodeId
{
    public string NodeId { get; set; } = string.Empty;

    public NodeJoinedMessage(string nodeId)
    {
        NodeId = nodeId;
    }
}
