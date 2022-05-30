namespace SlimCluster.Membership.Swim.Messages;

public class NodeLeftMessage : IHasNodeId
{
    public string NodeId { get; set; } = string.Empty;

    public NodeLeftMessage(string nodeId) => NodeId = nodeId;
}
