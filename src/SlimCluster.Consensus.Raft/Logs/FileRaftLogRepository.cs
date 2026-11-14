namespace SlimCluster.Consensus.Raft.Logs;

using System.Text.Json;

/// <summary>
/// A durable, uncompacted log for small clusters. Each update atomically replaces the local
/// log file. Use a dedicated path on durable storage for each node.
/// </summary>
public sealed class FileRaftLogRepository : IDurableRaftLogRepository
{
    private sealed record LogState(LogEntry[] Entries, int CommittedIndex);

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile LogState _state;

    public FileRaftLogRepository(string path)
    {
        _path = Path.GetFullPath(path);
        try
        {
            using var stream = File.OpenRead(_path);
            _state = JsonSerializer.Deserialize<LogState>(stream)
                ?? throw new InvalidDataException("The Raft log file is empty.");
        }
        catch (FileNotFoundException)
        {
            _state = new LogState(Array.Empty<LogEntry>(), 0);
        }
    }

    public LogIndex LastIndex => _state.Entries.LastOrDefault() is { } entry
        ? new LogIndex(entry.Index, entry.Term)
        : new LogIndex(0, 0);
    public LogIndex LastCompactedIndex => new(0, 0);
    public int FirstAvailableIndex => 1;
    public int CommitedIndex => _state.CommittedIndex;

    public int GetTermAtIndex(int index) => index == 0 ? 0 : _state.Entries[index - 1].Term;

    public Task<IReadOnlyList<LogEntry>> GetLogsAtIndex(int index, int count)
        => Task.FromResult<IReadOnlyList<LogEntry>>(_state.Entries.AsSpan(index - 1, count).ToArray());

    public async Task<int> Append(int term, byte[] entry)
    {
        var state = await Update(current =>
        {
            var logEntry = new LogEntry(current.Entries.Length + 1, term, entry);
            return current with { Entries = current.Entries.Append(logEntry).ToArray() };
        });
        return state.Entries.Length;
    }

    public Task Append(IEnumerable<LogEntry> logs) => Update(current =>
    {
        var entries = current.Entries.ToList();
        foreach (var entry in logs)
        {
            if (entry.Index <= 0 || entry.Index > entries.Count + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(logs), "Raft log entries must be contiguous.");
            }

            if (entry.Index <= entries.Count)
            {
                if (entries[entry.Index - 1].Term == entry.Term)
                {
                    continue;
                }
                if (entry.Index <= current.CommittedIndex)
                {
                    throw new InvalidOperationException($"Cannot overwrite committed entry {entry.Index}.");
                }
                entries.RemoveRange(entry.Index - 1, entries.Count - entry.Index + 1);
            }
            entries.Add(entry);
        }
        return entries.SequenceEqual(current.Entries) ? current : current with { Entries = entries.ToArray() };
    });

    public Task Commit(int index) => Update(current =>
    {
        if (index < current.CommittedIndex || index > current.Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return current.CommittedIndex == index ? current : current with { CommittedIndex = index };
    });

    public Task EraseBefore(int index) => throw new NotSupportedException("The file log retains its full history.");
    public Task InstallSnapshot(LogIndex lastIncludedIndex) => throw new NotSupportedException("The file log retains its full history.");

    private async Task<LogState> Update(Func<LogState, LogState> change)
    {
        await _writeLock.WaitAsync();
        try
        {
            var next = change(_state);
            if (ReferenceEquals(next, _state))
            {
                return next;
            }
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, next);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, _path, overwrite: true);
                _state = next;
            }
            finally
            {
                File.Delete(temporaryPath);
            }
            return next;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
