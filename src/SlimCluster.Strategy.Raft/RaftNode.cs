using SlimCluster.Membership;
using System.Collections.Generic;

namespace SlimCluster.Strategy.Raft
{
    public class RaftNode
    {
        private readonly IClusterMembership clusterMembership;

        #region persistent state

        public int CurrentTerm { get; protected set; }
        public string? VotedFor { get; protected set; }
        public IList<LogEntry> Log { get; protected set; }
        public Snapshot? Snapshot { get; protected set; }

        #endregion

        #region volatile state 

        public int CommitIndex { get; protected set; }
        public int LastApplied { get; protected set; }

        #endregion

        public RaftLeaderState? LeaderState { get; protected set; }
        public RaftCandidateState? CandidateState { get; protected set; }

        public RaftNode(IClusterMembership clusterMembership)
        {
            this.clusterMembership = clusterMembership;
            
            CurrentTerm = 0;
            VotedFor = null;
            Log = new List<LogEntry>();
            Snapshot = null;

            CommitIndex = 0;
            LastApplied = 0;

            LeaderState = null;
            CandidateState = null;
        }
    }
}

