namespace SlimCluster.Consensus.Raft;

using System.Collections.Concurrent;
using System.Diagnostics;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Host.Common;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Serialization;

public record FollowerReplicatonState
{
    /// <summary>
    /// For each server, index of the next log entry to send to that server (initialized to leader last log index + 1)
    /// </summary>
    public int NextIndex { get; set; }

    /// <summary>
    /// For each server, index of highest log entry known to be replicated on server (initialized to 0, increases monotonically)
    /// </summary>
    public int MatchIndex { get; set; }

    /// <summary>
    /// Last point in time when the <see cref="AppendEntriesRequest"/> was sent to the follower.
    /// </summary>
    public DateTimeOffset? LastAppendRequest { get; set; }
}

public delegate Task OnNewerTermDiscovered(int term, INode node);

public class RaftLeaderState : TaskLoop, IRaftClientRequestHandler, IDurableComponent
{
    private readonly ILogger<RaftLeaderState> _logger;
    private readonly RaftConsensusOptions _options;
    private readonly IClusterMembership _clusterMembership;
    private readonly ILogRepository _logRepository;
    private readonly IStateMachine _stateMachine;
    private readonly IMessageSender _messageSender;
    private readonly ISerializer _logSerializer;
    private readonly ITime _time;
    private readonly OnNewerTermDiscovered _onNewerTermDiscovered;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<object?>> _pendingCommandResults;

    public int Term { get; protected set; }
    public IDictionary<string, FollowerReplicatonState> ReplicationStateByNode { get; protected set; }

    public RaftLeaderState(
        ILogger<RaftLeaderState> logger,
        int term,
        RaftConsensusOptions options,
        IClusterMembership clusterMembership,
        IMessageSender messageSender,
        ILogRepository logRepository,
        IStateMachine stateMachine,
        ISerializer logSerializer,
        ITime time,
        OnNewerTermDiscovered onNewerTermDiscovered)
        : base(logger)
    {
        _logger = logger;
        _options = options;
        _clusterMembership = clusterMembership;
        _logSerializer = logSerializer;
        _logRepository = logRepository;
        _stateMachine = stateMachine;
        _messageSender = messageSender;
        _time = time;
        _onNewerTermDiscovered = onNewerTermDiscovered;
        _pendingCommandResults = new ConcurrentDictionary<int, TaskCompletionSource<object?>>();

        Term = term;
        ReplicationStateByNode = new Dictionary<string, FollowerReplicatonState>();
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        var tasks = new List<Task>();

        var lastIndex = _logRepository.LastIndex;
        var now = _time.Now;
        var idleRun = false;

        // for each cluster member - replicate logs 
        foreach (var member in _clusterMembership.OtherMembers)
        {
            var followerReplicationState = EnsureReplicationState(member.Node.Id);

            // check if there are new logs to replicate
            var sendNewLogEntries = lastIndex.Index >= followerReplicationState.NextIndex;
            // check if first ping after election
            var sendFirstPing = followerReplicationState.LastAppendRequest == null; // never sent an AppendEntriesRequest yet
            // check if ping sent in longer while
            var sendPing = followerReplicationState.LastAppendRequest == null
                || now >= followerReplicationState.LastAppendRequest.Value.Add(_options.LeaderPingInterval); // time to send ping to not get overthrown by another node

            if (sendNewLogEntries || sendPing)
            {
                var task = ReplicateLogWithFollower(lastIndex, followerReplicationState, member.Node, skipEntries: sendFirstPing, token);
                tasks.Add(task);
            }
        }

        if (tasks.Count > 0)
        {
            // idle run if all were idle runs
            await Task.WhenAll(tasks);
            idleRun = false;
        }

        // ToDo: Remove lost nodes from ReplicationStateByNode (after some idle time)

        // check if log replicated to majorty, if so then
        //   1. apply to state machione
        //   2. notify that commit index changed

        var commitedIndex = _logRepository.CommitedIndex;
        var highestReplicatedIndex = FindMajorityReplicatedIndex();
        if (highestReplicatedIndex > commitedIndex)
        {
            var logIndexStart = commitedIndex + 1;
            var logCount = highestReplicatedIndex - commitedIndex;
            var logs = await _logRepository.GetLogsAtIndex(logIndexStart, logCount);

            for (var i = 0; i < logCount; i++)
            {
                var index = logIndexStart + i;
                var result = await _stateMachine.Apply(command: logs[i], index: index);
                await _logRepository.Commit(index);

                // Signal that this changed and pass result
                if (_pendingCommandResults.TryGetValue(index, out var tcs))
                {
                    tcs.TrySetResult(result);
                }
            }
        }

        // idle run
        return idleRun;
    }

    private int FindMajorityReplicatedIndex()
    {
        var majorityCount = _options.NodeCount / 2;

        var orderedMatchIndexes = ReplicationStateByNode.Values.Select(x => x.MatchIndex).OrderByDescending(x => x).ToList();

        var majorityMatchIndex = orderedMatchIndexes.FirstOrDefault(matchIndex =>
        {
            return orderedMatchIndexes.Count(x => x >= matchIndex) > majorityCount;
        });

        return Math.Max(majorityMatchIndex, _logRepository.CommitedIndex);
    }

    protected internal async Task ReplicateLogWithFollower(LogIndex lastIndex, FollowerReplicatonState followerReplicationState, INode followerNode, bool skipEntries, CancellationToken token)
    {
        var prevLogIndex = followerReplicationState.NextIndex - 1;

        var req = new AppendEntriesRequest
        {
            Term = Term,
            LeaderId = _clusterMembership.SelfMember.Node.Id,
            LeaderCommitIndex = _logRepository.CommitedIndex,
            PrevLogIndex = prevLogIndex,
            PrevLogTerm = prevLogIndex == 0 ? 0 : _logRepository.GetTermAtIndex(prevLogIndex)
        };

        if (!skipEntries && lastIndex.Index > prevLogIndex)
        {
            // ToDo: Intro option for max logs send
            var logsCount = Math.Max(lastIndex.Index - prevLogIndex, 1);
            var logs = logsCount > 0
                ? await _logRepository.GetLogsAtIndex(prevLogIndex + 1, logsCount)
                : null;

            req.Entries = logs?.Select(_logSerializer.Serialize)?.ToList();
        }

        try
        {
            _logger.LogDebug("{Node}: Sending {MessageName} with PrevLogIndex = {PrevLogIndex}, PrevLogTerm = {PrevLogTerm}, LogCount = {LogCount}", followerNode, nameof(AppendEntriesRequest), req.PrevLogIndex, req.PrevLogTerm, req.Entries?.Count ?? 0);
            // Note: setting the request timeout to match the leader timeout - now point in waiting longer
            var resp = await _messageSender.SendRequest(req, followerNode.Address, timeout: _options.LeaderTimeout);

            // when the response arrives, update the timestamp of when the request was sent
            followerReplicationState.LastAppendRequest = _time.Now;

            if (resp.Success)
            {
                var count = req.Entries?.Count ?? 0;
                followerReplicationState.NextIndex += count;
                var newMatchIndex = prevLogIndex + count;
                if (newMatchIndex != followerReplicationState.MatchIndex)
                {
                    followerReplicationState.MatchIndex = newMatchIndex;
                    _logger.LogInformation("{Node}: Follower has log match until MatchIndex = {MatchIndex}", followerNode, followerReplicationState.MatchIndex);
                }
            }
            else
            {
                // if higher term discoverd
                if (resp.Term > Term)
                {
                    await _onNewerTermDiscovered(resp.Term, followerNode);
                    return;
                }

                _logger.LogDebug("{Node}: Follower log does not match at MatchIndex = {MatchIndex}", followerNode, followerReplicationState.MatchIndex);
                // logs dont match for the specified index, will retry on next loop run with prev index
                followerReplicationState.NextIndex--;
            }
        }
        catch (OperationCanceledException)
        {
            // Will retry next time
            _logger.LogWarning("{Node}: Did not recieve {MessageName} in time, will retry...", followerNode, nameof(AppendEntriesResponse));

            // The response did not arrive, account for the wait time we already lost to not keep on calling the possibly failed follower
            followerReplicationState.LastAppendRequest = _time.Now;
        }
    }

    private FollowerReplicatonState EnsureReplicationState(string nodeId)
    {
        if (!ReplicationStateByNode.TryGetValue(nodeId, out var replicatonState))
        {
            replicatonState = new FollowerReplicatonState
            {
                NextIndex = _logRepository.LastIndex.Index + 1,
                MatchIndex = 0
            };
            ReplicationStateByNode.Add(nodeId, replicatonState);
        }
        return replicatonState;
    }

    public async Task<object?> OnClientRequest(object command, CancellationToken token)
    {
        using var _ = _logger.BeginScope("Command {Command} processing", command.GetType().Name);

        // Append log to local node (must be thread-safe)
        var commandIndex = await _logRepository.Append(Term, command);
        _logger.LogTrace("Appended command at index {Index} in term {Term}", commandIndex, Term);

        var requestTimer = Stopwatch.StartNew();

        var tcs = new TaskCompletionSource<object?>();
        _pendingCommandResults.TryAdd(commandIndex, tcs);
        try
        {
            // Wait until index committed (replicated to majority of nodes) and applied to state machine
            while (requestTimer.Elapsed < _options.RequestTimeout)
            {
                token.ThrowIfCancellationRequested();

                var t = await Task.WhenAny(tcs.Task, Task.Delay(100, token));

                if (t == tcs.Task)
                {
                    var result = await tcs.Task;
                    _logger.LogInformation("Command {Command} result is {Result}", command, result);
                    return result;
                }
            }
        }
        finally
        {
            // Attempt to cancel if still was pending
            tcs.TrySetCanceled();

            // Clean up pending commands
            _pendingCommandResults.TryRemove(commandIndex, out var _);
        }
        return null;
    }

    #region IDurableComponent

    public void OnStateRestore(IStateReader state)
    {
        _logger.LogInformation("Restoring state");

        Term = state.Get<int>("leaderTerm");
        ReplicationStateByNode = state.Get<IDictionary<string, FollowerReplicatonState>>("leaderReplicationState") ?? new Dictionary<string, FollowerReplicatonState>();
    }

    public void OnStatePersist(IStateWriter state)
    {
        _logger.LogInformation("Persisting state");

        state.Set("leaderTerm", Term);
        state.Set("leaderReplicationState", ReplicationStateByNode);
    }

    #endregion
}
