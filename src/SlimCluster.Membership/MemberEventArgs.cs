namespace SlimCluster.Membership;

public class MemberEventArgs : EventArgs
{
    public INode Node { get; }

    public DateTimeOffset Timestamp { get; }

    public MemberEventArgs(INode node, DateTimeOffset timestamp)
    {
        Node = node;
        Timestamp = timestamp;
    }
}
