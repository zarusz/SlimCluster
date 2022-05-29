namespace SlimCluster.Membership
{
    using System;

    public interface IMember
    {
        INode Node { get; }
        DateTimeOffset Joined { get; }
        DateTimeOffset LastSeen { get; }
    }
}
