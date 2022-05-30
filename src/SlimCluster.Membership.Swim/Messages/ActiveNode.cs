namespace SlimCluster.Membership.Swim.Messages;

public class ActiveNode : IHasNodeId, IHasNodeAddress
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeAddress { get; set; } = string.Empty;
}
