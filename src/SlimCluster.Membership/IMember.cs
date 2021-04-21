namespace SlimCluster.Membership
{
    using System;

    public interface IMember
    {
        INode Node { get; }
        DateTime Joined { get; }
        DateTime LastSeen { get; }
    }

    public class Member : IMember
    {
        public INode Node { get; protected set; }
        public DateTime Joined { get; protected set; }
        public DateTime LastSeen { get; set; }

        public Member(INode node, DateTime joined)
        {
            Node = node;
            Joined = joined;
            LastSeen = joined;
        }
    }
}
