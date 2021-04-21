namespace SlimCluster.Membership
{
    using System.Collections.Generic;

    public interface IClusterMembership
    {
        /// <summary>
        /// Represents the logical cluster id this membership represents.
        /// </summary>
        string ClusterId { get; }

        /// <summary>
        /// Gets the current members snapshot
        /// </summary>
        IReadOnlyCollection<IMember> Members { get; }

        void OnKeepAlive(INode node);

        event MemberJoinedEventHandler? MemberJoined;
        event MemberLeftEventHandler? MemberLeft;

        public delegate void MemberJoinedEventHandler(object sender, MemberEventArgs e);
        public delegate void MemberLeftEventHandler(object sender, MemberEventArgs e);
    }
}
