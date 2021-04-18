using System.Collections.Generic;

namespace SlimCluster.Strategy.Raft
{
    public class RaftLeaderState
    {
        public IDictionary<string, int> NextIndex { get; protected set; }
        public IDictionary<string, int> MatchIndex { get; protected set; }

        public RaftLeaderState()
        {
            NextIndex = new Dictionary<string, int>();
            MatchIndex = new Dictionary<string, int>();
        }
    }

}
