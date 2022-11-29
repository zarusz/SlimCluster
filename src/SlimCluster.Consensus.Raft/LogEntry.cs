namespace SlimCluster.Consensus.Raft;

public class LogEntry
{
    public int Index { get; set; }
    public int Term { get; set; }
    /// <summary>
    /// Command for the <see cref="IStateMachine"/>.
    /// </summary>
    public object Command { get; set; }

    public LogEntry(int index, int term, object command)
    {
        Index = index;
        Term = term;
        Command = command;
    }
}
