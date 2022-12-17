namespace SlimCluster.Membership.Swim;

public class SwimMemberSelf : SwimMember, ICurrentNode
{
    public SwimMemberSelf(string id, IAddress address, ITime time, ILoggerFactory loggerFactory)
        : base(id, address, time.Now, SwimMemberStatus.Active, notifyStatusChanged: null, loggerFactory.CreateLogger<SwimMember>())
    {
    }
}
