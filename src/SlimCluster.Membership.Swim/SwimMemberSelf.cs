namespace SlimCluster.Membership.Swim
{
    public class SwimMemberSelf
    {
        public string Id { get; }
        public int Incarnation { get; set; }

        public SwimMemberSelf(string id, int incarnation)
        {
            Id = id;
            Incarnation = incarnation;
        }
    }

}
