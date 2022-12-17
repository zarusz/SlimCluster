namespace SlimCluster.Consensus.Raft.Logs;

public class LogIndex
{
    public int Index { get; set; }
    public int Term { get; set; }

    public LogIndex()
    {
    }

    public LogIndex(int index, int term)
    {
        Index = index;
        Term = term;
    }

    public override string ToString() => $"{GetType().Name}(Index={Index},Term={Term})";
}

