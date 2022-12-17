namespace SlimCluster.Consensus.Raft.Logs;

public interface ILogRepository
{
    LogIndex LastIndex { get; }
    int CommitedIndex { get; }
    Task Append(int index, int term, IEnumerable<object> logs);
    /// <summary>
    /// Appends log under the given term and returns the assigned index.
    /// </summary>
    /// <param name="term"></param>
    /// <param name="log"></param>
    /// <returns></returns>
    Task<int> Append(int term, object log);
    /// <summary>
    /// Commits up to a certain log and applies the all the logs until this point to the state machine
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    Task Commit(int index);
    int GetTermAtIndex(int index);
    Task EraseBefore(int index);
    Task<IReadOnlyList<object>> GetLogsAtIndex(int index, int count);
}

public interface ISnapshot
{
    LogIndex LastIndex { get; }
}
