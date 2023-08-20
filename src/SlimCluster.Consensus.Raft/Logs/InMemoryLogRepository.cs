namespace SlimCluster.Consensus.Raft.Logs;

using System;

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
            ?? throw new ArgumentOutOfRangeException(nameof(index), index, null);

        return log.Term;
    }

    public virtual Task<LogEntry> GetByIndex(int index)
    {
        if (index < _logsStartIndex || index > _lastIndex.Index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, null);
        }

        var log = _logs.ElementAt(index - _logsStartIndex);

        return Task.FromResult(log);
    }

    public virtual Task<int> Append(int term, byte[] entry)
    {
        var index = _lastIndex.Index + 1;
        var logEntry = new LogEntry(index, term, entry);
        _logs.AddLast(logEntry);

        _lastIndex = new LogIndex(index: index, term: term);

        return Task.FromResult(index);
    }

    public virtual Task Append(IEnumerable<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Index < _logsStartIndex || entry.Index > LastIndex.Index + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(entry.Index), entry.Index, null);
            }

            // Remove all starting at index
            while (_logs.Count + _logsStartIndex > entry.Index)
            {
                _logs.RemoveLast();
            }

            _logs.AddLast(entry);
            _lastIndex = entry;
        }

        return Task.CompletedTask;
    }

    public virtual Task Commit(int index)
    {
        if (index > _lastIndex.Index || _commitedIndex > index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, null);
        }
        _commitedIndex = index;
        return Task.CompletedTask;
    }

    public Task EraseBefore(int index)
    {
        if (index > _lastIndex.Index && _logsStartIndex > index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, null);
        }

        while (_logsStartIndex < index)
        {
            _logs.RemoveFirst();
            _logsStartIndex++;
        }

        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyList<LogEntry>> GetLogsAtIndex(int index, int count)
    {
        if (index < _logsStartIndex || (index - _logsStartIndex) + count > _logs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, null);
        }

        return Task.FromResult<IReadOnlyList<LogEntry>>(_logs.Skip(index - _logsStartIndex).Take(count).ToList());
    }
}
