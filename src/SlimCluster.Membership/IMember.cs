namespace SlimCluster.Membership
{
    using System;

    public interface IMember
    {
        INode Node { get; }
        DateTimeOffset Joined { get; }
        DateTimeOffset LastSeen { get; }
    }

    public class Member : IMember
    {
        public INode Node { get; protected set; }
        public DateTimeOffset Joined { get; protected set; }
        public DateTimeOffset LastSeen { get; protected set; }

        public Member(INode node, DateTimeOffset joined)
        {
            Node = node;
            Joined = joined;
            LastSeen = joined;
        }
    }
}
