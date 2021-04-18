using System.Collections.Generic;

namespace SlimCluster.Strategy.Raft
{
    public class RaftCandidateState
    {
        public int Term { get; set; }
        public ISet<string> RecivedVotesFrom { get; set; }

        public RaftCandidateState(int term)
        {
            Term = term;
            RecivedVotesFrom = new HashSet<string>();
        }
    }
}