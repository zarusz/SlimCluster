namespace SlimCluster.Consensus.Raft;

using SlimCluster.Consensus.Raft.Logs;

public class AppendEntriesRequest : RaftMessage, IHasTerm, IRequest<AppendEntriesResponse>
{
    /// <summary>
    /// The leader's current term.
    /// </summary>
    public int Term { get; set; }
    /// <summary>
    /// The leader ID, so followers can redrect clients to.
    /// </summary>
    public string LeaderId { get; set; } = string.Empty;
    /// <summary>
    /// The leader's current commit index.
    /// </summary>
    public int LeaderCommitIndex { get; set; }
    /// <summary>
    /// The leader's previous log entry
    /// </summary>
    public int PrevLogIndex { get; set; }
    /// <summary>
    /// The leader's previous log entry
    /// </summary>
    public int PrevLogTerm { get; set; }
    /// <summary>
    /// New entries to add or null if ping message.
    /// </summary>
    public IReadOnlyCollection<LogEntry>? Entries { get; set; }

    public override string ToString() => $"{GetType().Name}(Term={Term},PrevLogIndex={PrevLogIndex},PrevLogTerm={PrevLogTerm},Entries={Entries?.Count ?? 0})";
}
