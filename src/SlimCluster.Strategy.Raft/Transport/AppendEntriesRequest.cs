using System.Collections.Generic;

namespace SlimCluster.Strategy.Raft
{
    public class AppendEntriesRequest
    {
        public int Term { get; set; }
        public string? LeaderId { get; set; }
        public int PrevLogIndex { get; set; }
        public int PrevLogTerm { get; set; }
        public IReadOnlyCollection<object>? Entries { get; set; }
        public int LeaderCommitIndex { get; set; }
    }
}
