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

        event MemberJoindedEventHandler? MemberJoined;
        event MemberDepartedEventHandler? MemberDeparted;

        public delegate void MemberJoindedEventHandler(object sender, MemberEventArgs e);
        public delegate void MemberDepartedEventHandler(object sender, MemberEventArgs e);
    }
}
