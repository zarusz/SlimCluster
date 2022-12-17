namespace SlimCluster.Consensus.Raft.Logs;

public class InMemoryLogRepository : ILogRepository
{
    private LogIndex _lastIndex = new(0, 0);
    private int _commitedIndex = 0;

    private int _logsStartIndex = 1;
    private readonly LinkedList<LogEntry> _logs = new();

    public virtual LogIndex LastIndex => _lastIndex;
    public virtual int CommitedIndex => _commitedIndex;

    public virtual int GetTermAtIndex(int index)
    {
        var log = _logs.ElementAtOrDefault(index - _logsStartIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(index));

        return log.Term;
    }

    public virtual Task<LogEntry> GetByIndex(int index)
    {
        if (index < _logsStartIndex || index > _lastIndex.Index)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var log = _logs.ElementAt(index - _logsStartIndex);

        return Task.FromResult(log);
    }

    public virtual Task<int> Append(int term, object entry)
    {
        var index = _lastIndex.Index + 1;
        var logEntry = new LogEntry(index, term, entry);
        _logs.AddLast(logEntry);

        _lastIndex = new LogIndex(index: index, term: term);

        return Task.FromResult(index);
    }

    public virtual Task Append(int index, int term, IEnumerable<object> entries)
    {
        if (index < _logsStartIndex || index > _lastIndex.Index)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        // Remove all starting at index
        while (_logs.Count + _logsStartIndex > index)
        {
            _logs.RemoveLast();
        }

        foreach (var entry in entries)
        {
            _logs.AddLast(new LogEntry(index++, term, entry));
        }

        _lastIndex = new(index, term);

        return Task.CompletedTask;
    }

    public virtual Task Commit(int index)
    {
        if (index > _lastIndex.Index || _commitedIndex > index)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        _commitedIndex = index;
        return Task.CompletedTask;
    }

    public Task EraseBefore(int index)
    {
        if (index > _lastIndex.Index && _logsStartIndex > index)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        while (_logsStartIndex < index)
        {
            _logs.RemoveFirst();
            _logsStartIndex++;
        }

        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyList<object>> GetLogsAtIndex(int index, int count)
    {
        if (index < _logsStartIndex || (index - _logsStartIndex) + count > _logs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        } 

        return Task.FromResult<IReadOnlyList<object>>(_logs.Skip(index - _logsStartIndex).Take(count).Select(x => x.Entry).ToList());
    }
}
