namespace SlimCluster.Membership.Swim;

public interface IMembershipEventListener
{
    Task OnNodeJoined(string nodeId, IPEndPoint senderEndPoint);
    Task OnNodeLeft(string nodeId);
}
