namespace SlimCluster.Membership;

public interface IMember
{
    INode Node { get; }
    DateTimeOffset Joined { get; }
    DateTimeOffset LastSeen { get; }
}
