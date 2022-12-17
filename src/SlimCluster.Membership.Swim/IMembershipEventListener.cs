namespace SlimCluster.Membership.Swim;

public interface IMembershipEventListener
{
    Task OnNodeJoined(string nodeId, IAddress senderAddress);
    Task OnNodeLeft(string nodeId);
}
