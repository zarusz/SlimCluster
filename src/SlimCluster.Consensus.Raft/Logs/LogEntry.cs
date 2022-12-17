namespace SlimCluster.Consensus.Raft.Logs;

public class LogEntry : LogIndex
{
    /// <summary>
    /// Entry for the <see cref="IStateMachine"/>.
    /// </summary>
    public object Entry { get; set; }

    public LogEntry(int index, int term, object entry) : base(index: index, term: term)
    {
        Entry = entry;
    }
}
