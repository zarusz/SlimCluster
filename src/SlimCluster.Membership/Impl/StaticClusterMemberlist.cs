namespace SlimCluster.Membership
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a static list of nodes that is preconfigured up-front and static (does not change over time).
    /// </summary>
    public class StaticClusterMemberlist : IClusterMembership
    {
        public string ClusterId { get; protected set; }

        public IReadOnlyCollection<IMember> Members { get; protected set; }

        public event IClusterMembership.MemberJoinedEventHandler? MemberJoined;
        public event IClusterMembership.MemberLeftEventHandler? MemberLeft;
        public event IClusterMembership.MemberStatusChangedEventHandler? MemberStatusChanged;

        public StaticClusterMemberlist(string clusterId, IEnumerable<INode> nodes)
        {
            ClusterId = clusterId;
            Members = nodes.Select(x => new Member(x, DateTime.UtcNow)).ToList();
        }

        public void OnKeepAlive(INode node)
        {
            foreach (var member in Members.Where(x => x.Node == node)) {
                //((Member)member).LastSeen = DateTimeOffset.Now;
            }
        }

        public Task Start() => Task.CompletedTask;

        public Task Stop() => Task.CompletedTask;
    }
}
