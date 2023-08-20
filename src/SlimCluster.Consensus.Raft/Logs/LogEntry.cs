namespace SlimCluster.Consensus.Raft.Logs;

public class LogEntry : LogIndex
{
    /// <summary>
    /// Entry for the <see cref="IStateMachine"/>.
    /// </summary>
    public byte[] Entry { get; set; }

    public LogEntry()
    {
        Entry = Array.Empty<byte>();
    }

    public LogEntry(int index, int term, byte[] entry)
        : base(index: index, term: term)
    {
        Entry = entry;
    }
}
