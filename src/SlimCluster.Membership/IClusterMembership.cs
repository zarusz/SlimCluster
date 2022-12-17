namespace SlimCluster.Membership;

public interface IClusterMembership
{
    /// <summary>
    /// Represents the logical cluster id this membership represents.
    /// </summary>
    string ClusterId { get; }

    /// <summary>
    /// Gets the current members snapshot.
    /// </summary>
    IReadOnlyCollection<IMember> Members { get; }

    /// <summary>
    /// Gets the current members snapshot excluding self.
    /// </summary>
    IReadOnlyCollection<IMember> OtherMembers { get; }

    /// <summary>
    /// Gets the members representing this node.
    /// </summary>
    IMember SelfMember { get; }

    event MemberJoinedEventHandler? MemberJoined;
    event MemberLeftEventHandler? MemberLeft;
    event MemberChangedEventHandler? MemberChanged;
    event MemberStatusChangedEventHandler? MemberStatusChanged;

    public delegate void MemberJoinedEventHandler(object sender, MemberEventArgs e);
    public delegate void MemberLeftEventHandler(object sender, MemberEventArgs e);
    public delegate void MemberChangedEventHandler(object sender, MemberEventArgs e);
    public delegate void MemberStatusChangedEventHandler(object sender, MemberEventArgs e);
}
