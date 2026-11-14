namespace SlimCluster.Consensus.Raft;

using System.Collections.Concurrent;

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

    internal DateTimeOffset? LastSuccessfulAppend { get; set; }
    internal IAddress? LastSuccessfulAddress { get; set; }
}

public delegate Task OnNewerTermDiscovered(int term, INode node);

public class RaftLeaderState : TaskLoop, IRaftClientRequestHandler, IDurableComponent
{
    private readonly ILogger<RaftLeaderState> _logger;
    private readonly RaftConsensusOptions _options;
    private readonly RaftVoterConfiguration _voters;
    private readonly RaftVoterState? _voterState;
    private readonly RaftAutomaticVoterReplacement? _automaticReplacement;
    private readonly IClusterMembership _clusterMembership;
    private readonly ILogRepository _logRepository;
    private readonly IStateMachine _stateMachine;
    private readonly IMessageSender _messageSender;
    private readonly ISerializer _logSerializer;
    private readonly ITime _time;
    private readonly OnNewerTermDiscovered _onNewerTermDiscovered;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<object?>> _pendingCommandResults;
    private readonly SemaphoreSlim _commandRegistration = new(1, 1);
    private volatile bool _higherTermObserved;
    private volatile bool _retired;
    private VoterChangeRequest? _voterChangeRequest;

    private sealed record AutomaticReplacement(string RemovedId, string LearnerId, IAddress Address, DateTimeOffset ProbedAt);

    private sealed class VoterChangeRequest(string[] target, int catchUpIndex, AutomaticReplacement? replacement = null)
    {
        private const int Waiting = 0;
        private const int Logging = 1;
        private const int Canceled = 2;
        private int _stage;

        public string[] Target { get; } = target;
        public int CatchUpIndex { get; } = catchUpIndex;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AutomaticReplacement? Replacement { get; } = replacement;

        public bool StartLogging() => Interlocked.CompareExchange(ref _stage, Logging, Waiting) != Canceled;
        public bool CancelWhileWaiting() => Interlocked.CompareExchange(ref _stage, Canceled, Waiting) == Waiting;
    }

    internal bool Retired => _retired;

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
        : this(logger, term, options, clusterMembership, messageSender, logRepository, stateMachine, logSerializer, time,
            onNewerTermDiscovered, new RaftVoterConfiguration(options, clusterMembership.SelfMember.Node.Id))
    {
    }

    internal RaftLeaderState(
        ILogger<RaftLeaderState> logger,
        int term,
        RaftConsensusOptions options,
        IClusterMembership clusterMembership,
        IMessageSender messageSender,
        ILogRepository logRepository,
        IStateMachine stateMachine,
        ISerializer logSerializer,
        ITime time,
        OnNewerTermDiscovered onNewerTermDiscovered,
        RaftVoterConfiguration voters,
        RaftVoterState? voterState = null)
        : base(logger)
    {
        _logger = logger;
        _options = options;
        _voters = voters;
        _voterState = voterState;
        if (voterState != null && options.AutomaticVoterReplacementDelay is { } delay)
        {
            _automaticReplacement = new RaftAutomaticVoterReplacement(delay, options.AutomaticVoterReplacementCandidate);
        }
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

    protected override async Task OnStarting()
    {
        await base.OnStarting();

        await _logRepository.Append(Term, Array.Empty<byte>()).ConfigureAwait(false);
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        if (_higherTermObserved || _retired)
        {
            return true;
        }

        var tasks = new List<Task>();

        var lastIndex = _logRepository.LastIndex;
        var now = _time.Now;
        var idleRun = true;

        // for each cluster member - replicate logs 
        var selfId = _clusterMembership.SelfMember.Node.Id;
        var view = _voterState?.Current;
        if (_automaticReplacement != null && view != null)
        {
            _automaticReplacement.Observe(view, _clusterMembership.OtherMembers, selfId, now);
        }
        var changeRequest = Volatile.Read(ref _voterChangeRequest);
        var committed = _logRepository.CommitedIndex;
        foreach (var member in _clusterMembership.OtherMembers
            .Where(member => member.Node.Id != selfId))
        {
            var id = member.Node.Id;
            var voter = view?.CanReplicateTo(id, committed) ?? _voters.Contains(id);
            if (!voter && changeRequest?.Target.Contains(id, StringComparer.Ordinal) != true)
            {
                continue;
            }
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
                var task = followerReplicationState.NextIndex < _logRepository.FirstAvailableIndex
                    ? InstallSnapshotOnFollower(followerReplicationState, member.Node, token)
                    : ReplicateLogWithFollower(lastIndex, followerReplicationState, member.Node, skipEntries: sendFirstPing, token);
                tasks.Add(task);
            }
        }

        if (tasks.Count > 0)
        {
            // idle run if all were idle runs
            await Task.WhenAll(tasks);
            idleRun = false;
        }

        if (!_higherTermObserved && !token.IsCancellationRequested)
        {
            idleRun &= await TryApplyLogs().ConfigureAwait(false);
            if (_voterState != null)
            {
                idleRun &= !await AdvanceVoterChange(_voterState, selfId, token);
            }
        }

        // idle run
        return idleRun;
    }

    private async Task<bool> TryApplyLogs()
    {
        var idleRun = true;

        // check if log replicated to majorty, if so then
        //   1. apply to state machione
        //   2. notify that commit index changed

        var commitedIndex = _logRepository.CommitedIndex;
        var highestReplicatedIndex = FindMajorityReplicatedIndex();
        if (highestReplicatedIndex > commitedIndex)
        {
            await ApplyLogs(commitedIndex, highestReplicatedIndex).ConfigureAwait(false);
            idleRun = false;
        }

        return idleRun;
    }

    private async Task ApplyLogs(int commitedIndex, int highestReplicatedIndex)
    {
        _logger.LogDebug("HighestReplicatedIndex: {HighestReplicatedIndex}, CommitedIndex: {ComittedIndex}", highestReplicatedIndex, commitedIndex);

        var logIndexStart = commitedIndex + 1;
        var logCount = highestReplicatedIndex - commitedIndex;
        var logs = await _logRepository.GetLogsAtIndex(logIndexStart, logCount).ConfigureAwait(false);

        for (var i = 0; i < logCount; i++)
        {
            var index = logIndexStart + i;

            var commandResult = await _stateMachine.Apply(_logRepository, logEntry: logs[i].Entry, logIndex: index, _logger, _logSerializer);

            // Signal that this changed and pass result
            await _commandRegistration.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_pendingCommandResults.TryGetValue(index, out var tcs))
                {
                    _logger.LogDebug("Set result to {Result}", commandResult);
                    tcs.TrySetResult(commandResult);
                }
            }
            finally
            {
                _commandRegistration.Release();
            }
        }
    }

    private int FindMajorityReplicatedIndex()
    {
        var selfId = _clusterMembership.SelfMember.Node.Id;
        var majorityIndex = _voterState?.Current.QuorumIndex(id => id == selfId ? _logRepository.LastIndex.Index : GetMatchIndex(id))
            ?? ReplicationStateByNode
            .Where(pair => _voters.Contains(pair.Key) && pair.Key != selfId)
            .Select(pair => pair.Value.MatchIndex)
            .Append(_logRepository.LastIndex.Index)
            .OrderByDescending(x => x)
            .Skip(_voters.Majority - 1)
            .FirstOrDefault();

        var committedIndex = _logRepository.CommitedIndex;
        return majorityIndex > committedIndex && _logRepository.GetTermAtIndex(majorityIndex) == Term
            ? majorityIndex
            : committedIndex;
    }

    private int GetMatchIndex(string id) => ReplicationStateByNode.TryGetValue(id, out var state) ? state.MatchIndex : 0;

    internal async Task ChangeVoters(IReadOnlyCollection<string> voterIds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var target = RaftVoterConfiguration.ValidateVoterIds(voterIds);
        var voterState = _voterState ?? throw new InvalidOperationException("Raft voter reconfiguration is disabled.");
        var view = voterState.Current;
        if (_higherTermObserved || _retired || view.IsJoint || view.Index > _logRepository.CommitedIndex)
        {
            throw new InvalidOperationException("Raft is already changing voters or leadership has changed.");
        }
        if (target.SequenceEqual(view.NewIds, StringComparer.Ordinal))
        {
            return;
        }

        var retired = target.FirstOrDefault(id => !view.Contains(id) && voterState.HasEverVoted(id));
        if (retired != null)
        {
            throw new ArgumentException($"Raft voter '{retired}' has been retired. Use a fresh learner ID for readmission.", nameof(voterIds));
        }

        var visibleIds = _clusterMembership.OtherMembers.Select(member => member.Node.Id).ToHashSet(StringComparer.Ordinal);
        if (target.Any(id => !view.Contains(id) && !visibleIds.Contains(id)))
        {
            throw new ArgumentException("New voters must first be discovered through cluster membership.", nameof(voterIds));
        }

        var request = new VoterChangeRequest(target, _logRepository.LastIndex.Index);
        if (Interlocked.CompareExchange(ref _voterChangeRequest, request, null) != null)
        {
            throw new InvalidOperationException("Raft is already changing voters.");
        }

        try
        {
            await request.Completion.Task.WaitAsync(_options.RequestTimeout, token);
        }
        finally
        {
            if (request.CancelWhileWaiting())
            {
                Interlocked.CompareExchange(ref _voterChangeRequest, null, request);
            }
        }
    }

    private async Task<bool> AdvanceVoterChange(RaftVoterState state, string selfId, CancellationToken token)
    {
        var view = state.Current;
        var committed = _logRepository.CommitedIndex;
        if (view.Index > committed)
        {
            return false;
        }

        if (view.IsJoint)
        {
            await AppendVoterChange(state, new RaftVoterChange(view.OldIds, view.NewIds, false), token);
            return true;
        }

        var request = Volatile.Read(ref _voterChangeRequest);
        if (request != null && view.NewIds.SequenceEqual(request.Target, StringComparer.Ordinal))
        {
            Interlocked.CompareExchange(ref _voterChangeRequest, null, request);
            request.Completion.TrySetResult();
            request = null;
        }

        if (!view.Contains(selfId))
        {
            _retired = true;
            return false;
        }

        if (committed == 0 || _logRepository.GetTermAtIndex(committed) != Term)
        {
            return false;
        }

        if (request == null && _automaticReplacement != null)
        {
            return await TryProposeAutomaticReplacement(state, view, selfId, token);
        }

        if (request != null && request.Target.All(id => view.Contains(id) || GetMatchIndex(id) >= request.CatchUpIndex))
        {
            return await AppendVoterChange(state, new RaftVoterChange(view.NewIds, request.Target, true), token, request);
        }
        if (request?.Replacement is { } replacement)
        {
            CheckAutomaticReplacement(view, selfId, request, replacement);
        }
        return false;
    }

    private async Task<bool> TryProposeAutomaticReplacement(RaftVoterState state, RaftVoterView view, string selfId, CancellationToken token)
    {
        var policy = _automaticReplacement!;
        var now = _time.Now;
        var members = _clusterMembership.OtherMembers;
        policy.Observe(view, members, selfId, now);
        var removedId = policy.FindMissingVoter(now);
        if (removedId == null || now < policy.ProbeAgainAt || !HasRecentVoterQuorum(view, members, selfId, now))
        {
            return false;
        }

        policy.ProbeAgainAt = now + _options.LeaderPingInterval;
        var candidates = members.Where(member => !state.HasEverVoted(member.Node.Id) && policy.IsEligibleLearner(member, now))
            .OrderBy(member => member.Node.Id, StringComparer.Ordinal);
        var probes = candidates.Select(async member =>
        {
            var address = member.Node.Address;
            var isLearner = await ProbeLearner(address);
            return (Id: member.Node.Id, Address: address, IsLearner: isLearner);
        });
        foreach (var candidate in await Task.WhenAll(probes))
        {
            if (!candidate.IsLearner)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();
            var target = view.NewIds.Where(id => id != removedId).Append(candidate.Id).Order(StringComparer.Ordinal).ToArray();
            var replacement = new AutomaticReplacement(removedId, candidate.Id, candidate.Address, _time.Now);
            var request = new VoterChangeRequest(target, _logRepository.LastIndex.Index, replacement);
            if (Interlocked.CompareExchange(ref _voterChangeRequest, request, null) == null)
            {
                _logger.LogInformation("Preparing automatic Raft voter replacement from {RemovedVoter} to {Learner}", removedId, candidate.Id);
                return true;
            }
            break;
        }
        return false;
    }

    private async Task<bool> ProbeLearner(IAddress address)
    {
        try
        {
            var response = await _messageSender.SendRequest(new LearnerProbeRequest(), address, _options.LeaderPingInterval);
            return response.IsStandbyLearner;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool CheckAutomaticReplacement(RaftVoterView view, string selfId, VoterChangeRequest request, AutomaticReplacement replacement)
    {
        var now = _time.Now;
        var members = _clusterMembership.OtherMembers;
        var policy = _automaticReplacement!;
        policy.Observe(view, members, selfId, now);
        var learner = members.FirstOrDefault(member => member.Node.Id == replacement.LearnerId);
        var learnerReady = HadRecentAppend(replacement.LearnerId, replacement.Address, now);
        if (!policy.IsMissing(replacement.RemovedId, now)
            || learner == null || !replacement.Address.Equals(learner.Node.Address) || !policy.IsEligibleLearner(learner, now)
            || (!learnerReady && now - replacement.ProbedAt >= _options.LeaderTimeout))
        {
            if (request.CancelWhileWaiting())
            {
                Interlocked.CompareExchange(ref _voterChangeRequest, null, request);
            }
            return false;
        }

        return learnerReady && HasRecentVoterQuorum(view, members, selfId, now);
    }

    private bool HasRecentVoterQuorum(RaftVoterView view, IReadOnlyCollection<IMember> members, string selfId, DateTimeOffset now)
    {
        var replyingVoters = members.Where(member => view.Contains(member.Node.Id)
                && HadRecentAppend(member.Node.Id, member.Node.Address, now))
            .Select(member => member.Node.Id).ToHashSet(StringComparer.Ordinal);
        replyingVoters.Add(selfId);
        return view.HasQuorum(replyingVoters);
    }

    private bool HadRecentAppend(string id, IAddress address, DateTimeOffset now)
        => ReplicationStateByNode.TryGetValue(id, out var follower)
            && follower.LastSuccessfulAddress?.Equals(address) == true
            && follower.LastSuccessfulAppend is { } last
            && now - last >= TimeSpan.Zero && now - last <= _options.LeaderTimeout;

    private async Task<bool> AppendVoterChange(RaftVoterState state, RaftVoterChange change, CancellationToken token, VoterChangeRequest? request = null)
    {
        await _commandRegistration.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (request != null && ((request.Replacement is { } replacement
                    && !CheckAutomaticReplacement(state.Current, _clusterMembership.SelfMember.Node.Id, request, replacement))
                || !request.StartLogging()))
            {
                return false;
            }
            var index = _logRepository.LastIndex.Index + 1;
            await _logRepository.Append(new[] { new LogEntry(index, Term, Array.Empty<byte>()) { VoterChange = change } });
            state.Append(index, change);
            return true;
        }
        finally
        {
            _commandRegistration.Release();
        }
    }

    protected override async Task OnStopping()
    {
        var request = Interlocked.Exchange(ref _voterChangeRequest, null);
        if (request != null && request.Replacement == null)
        {
            request.Completion.TrySetException(new RaftClusterException("Leadership changed before the voter change was confirmed."));
        }
        await base.OnStopping();
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
            var logsCount = Math.Min(lastIndex.Index - prevLogIndex, 1);
            var logs = logsCount > 0
                ? await _logRepository.GetLogsAtIndex(prevLogIndex + 1, logsCount)
                : null;

            req.Entries = logs;
        }

        try
        {
            _logger.LogDebug("{Node}: Sending {MessageName} with PrevLogIndex = {PrevLogIndex}, PrevLogTerm = {PrevLogTerm}, LogCount = {LogCount}", followerNode, nameof(AppendEntriesRequest), req.PrevLogIndex, req.PrevLogTerm, req.Entries?.Count ?? 0);
            // Note: setting the request timeout to match the leader timeout - now point in waiting longer
            var address = followerNode.Address;
            var resp = await _messageSender.SendRequest(req, address, timeout: _options.LeaderPingInterval);

            // when the response arrives, update the timestamp of when the request was sent
            followerReplicationState.LastAppendRequest = _time.Now;

            if (resp.Term > Term)
            {
                if (_voterState?.Current.Contains(followerNode.Id) != false)
                {
                    _higherTermObserved = true;
                    await _onNewerTermDiscovered(resp.Term, followerNode);
                }
                return;
            }
            if (resp.Term < Term)
            {
                return;
            }

            if (resp.Success)
            {
                followerReplicationState.LastSuccessfulAppend = _time.Now;
                followerReplicationState.LastSuccessfulAddress = address;
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
                _logger.LogInformation("{Node}: Follower log does not match at MatchIndex = {MatchIndex}", followerNode, followerReplicationState.MatchIndex);
                // logs dont match for the specified index, will retry on next loop run with prev index
                followerReplicationState.NextIndex = Math.Max(1, followerReplicationState.NextIndex - 1);
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

    private async Task InstallSnapshotOnFollower(FollowerReplicatonState followerReplicationState, INode followerNode, CancellationToken token)
    {
        var snapshot = await _stateMachine.Snapshot().ConfigureAwait(false);
        var snapshotId = Guid.NewGuid();
        var offset = 0;

        while (offset < snapshot.Length || offset == 0)
        {
            token.ThrowIfCancellationRequested();

            var count = Math.Min(Math.Max(1, _options.SnapshotChunkSize), snapshot.Length - offset);
            var done = offset + count >= snapshot.Length;
            var req = new InstallSnapshotRequest
            {
                Term = Term,
                LeaderId = _clusterMembership.SelfMember.Node.Id,
                SnapshotId = snapshotId,
                LastIncludedIndex = _logRepository.LastCompactedIndex.Index,
                LastIncludedTerm = _logRepository.LastCompactedIndex.Term,
                Offset = offset,
                Data = count > 0 ? snapshot.Skip(offset).Take(count).ToArray() : Array.Empty<byte>(),
                Done = done
            };

            var resp = await _messageSender.SendRequest(req, followerNode.Address, timeout: _options.LeaderPingInterval).ConfigureAwait(false);
            followerReplicationState.LastAppendRequest = _time.Now;

            if (resp.Term > Term)
            {
                _higherTermObserved = true;
                await _onNewerTermDiscovered(resp.Term, followerNode).ConfigureAwait(false);
                return;
            }

            if (resp.Term < Term || !resp.Success)
            {
                return;
            }

            offset += count;
            if (done)
            {
                followerReplicationState.MatchIndex = req.LastIncludedIndex;
                followerReplicationState.NextIndex = req.LastIncludedIndex + 1;
                return;
            }
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
        if (_higherTermObserved || _retired || _voterState?.Current.Contains(_clusterMembership.SelfMember.Node.Id) == false)
        {
            throw new RaftClusterException("Leadership has changed.");
        }
        token.ThrowIfCancellationRequested();

        using var _ = _logger.BeginScope("Command {Command} processing", command.GetType().Name);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int commandIndex;
        // A fast commit can apply the entry before the client finishes registering its completion.
        var log = _logSerializer.Serialize(command);
        await _commandRegistration.WaitAsync(token).ConfigureAwait(false);
        try
        {
            commandIndex = await _logRepository.Append(Term, log).ConfigureAwait(false);
            _pendingCommandResults.TryAdd(commandIndex, tcs);
        }
        finally
        {
            _commandRegistration.Release();
        }
        _logger.LogTrace("Appended command at index {Index} in term {Term}", commandIndex, Term);

        try
        {
            var result = await tcs.Task.WaitAsync(_options.RequestTimeout, token).ConfigureAwait(false);
            _logger.LogInformation("Command {Command} result is {Result}", command, result);
            return result;
        }
        finally
        {
            // Attempt to cancel if still was pending
            tcs.TrySetCanceled();

            // Clean up pending commands
            _pendingCommandResults.TryRemove(commandIndex, out var _);
        }
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
