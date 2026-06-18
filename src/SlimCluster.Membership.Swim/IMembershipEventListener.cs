namespace SlimCluster.Membership.Swim;

public interface IMembershipEventListener
{
    Task OnNodeJoined(string nodeId, IAddress senderAddress);
    Task OnNodeLeft(string nodeId);
}

public interface IIncarnationMembershipEventListener : IMembershipEventListener
{
    Task OnNodeJoined(string nodeId, IAddress senderAddress, long incarnation);
    Task OnNodeLeft(string nodeId, long incarnation);
}
