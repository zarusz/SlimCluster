namespace SlimCluster.Membership
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

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

        event MemberJoinedEventHandler? MemberJoined;
        event MemberLeftEventHandler? MemberLeft;
        event MemberStatusChangedEventHandler? MemberStatusChanged;

        public delegate void MemberJoinedEventHandler(object sender, MemberEventArgs e);
        public delegate void MemberLeftEventHandler(object sender, MemberEventArgs e);
        public delegate void MemberStatusChangedEventHandler(object sender, MemberEventArgs e);

        /// <summary>
        /// Start the membership (the node will attempt to join).
        /// </summary>
        /// <returns></returns>
        Task Start();

        /// <summary>
        /// Stop the membership (the node will leave).
        /// </summary>
        /// <returns></returns>
        Task Stop();
    }
}
