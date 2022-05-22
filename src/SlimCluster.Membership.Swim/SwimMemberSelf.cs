namespace SlimCluster.Membership.Swim
{
    public class SwimMemberSelf : SwimMember
    {
        public SwimMemberSelf(string id, int incarnation, IPEndPointAddress address, ITime time)
            : base(id, address, time.Now, incarnation, SwimMemberStatus.Active, notifyStatusChanged: null)
        {
        }

        public void UpdateAddress(IPEndPointAddress address)
        {
            Address = address;
        }
    }
}
