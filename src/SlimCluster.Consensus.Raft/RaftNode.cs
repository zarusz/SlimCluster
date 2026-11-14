namespace SlimCluster.Consensus.Raft;

using System;
using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Host;
using SlimCluster.Host.Common;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Serialization;

public class RaftNode : TaskLoop, IMessageArrivedHandler, IAsyncDisposable, IDurableComponent, IClusterControlComponent, IRaftClientRequestHandler, IRaftVoterReconfiguration
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IClusterMembership _clusterMembership;
    private readonly IMessageSender _messageSender;
    private readonly RaftConsensusOptions _options;
    private readonly bool _bootstrapFromMembership;
    private readonly int _expectedVoterCount;
    private RaftVoterConfiguration? _voters;
    private RaftVoterConfiguration Voters => _voters ?? throw new InvalidOperationException("Raft bootstrap has not completed.");
    private RaftVoterState? _voterState;
    private bool _persistentStateDirty;
    private bool _restoredPersistentState;
    private IRaftBootstrapStore? _bootstrapStore;
    private bool _voterReadyReported;
    private bool _allVotersReady;
    private volatile bool _bootstrapComplete;
    private readonly ISerializer _logSerializer;
    private readonly ITime _time;
    private readonly IStateMachine _stateMachine;

    public RaftNodeStatus Status { get; protected set; }

    #region persistent state

    /// <summary>
    /// Latest term server has seen (initialized to 0 on first boot, increases monotonically)
    /// </summary>
    private int _currentTerm;
    /// <summary>
    /// CandidateId that received vote in current term (or null if none)
    /// </summary>
    private string? _votedFor;
    /// <summary>
    /// log entries; each entry contains command for state machine, and term when entry was received by leader(first index is 1)
    /// </summary>
    private readonly ILogRepository _logRepository;

    #endregion

    private RaftLeaderState? _leaderState;
    private RaftCandidateState? _candidateState;
    private RaftFollowerState? _followerState;
    private MemoryStream? _installingSnapshot;
    private Guid? _installingSnapshotId;
    private string? _installingSnapshotLeaderId;
    private int _installingSnapshotLastIncludedIndex;
    private int _installingSnapshotLastIncludedTerm;

    private readonly ConcurrentQueue<(RaftMessage Message, IAddress Address)> _messages = new();
    private readonly ConcurrentQueue<(int Term, INode Node)> _higherTerms = new();

    public int CurrentTerm => _currentTerm;

    public INode? LeaderNode
    {
        get
        {
            if (_leaderState != null && Status == RaftNodeStatus.Leader)
            {
                return _clusterMembership.SelfMember.Node;
            }
            if (_followerState != null && Status == RaftNodeStatus.Follower)
            {
                return _followerState.Leader;
            }
            return null;
        }
    }

    public RaftNode(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IClusterMembership clusterMembership,
        ITime time,
        ILogRepository logRepository,
        IMessageSender messageSender,
        IStateMachine stateMachine,
        IOptions<RaftConsensusOptions> options)
        : base(loggerFactory.CreateLogger<RaftNode>())
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = loggerFactory.CreateLogger<RaftNode>();
        _clusterMembership = clusterMembership;
        _time = time;
        _logRepository = logRepository;
        _messageSender = messageSender;
        _stateMachine = stateMachine;
        _options = options.Value;
        _expectedVoterCount = _options.NodeCount;
        if (_options.JoinAsLearner && !_options.EnableVoterReconfiguration)
        {
            throw new ArgumentException("Joining as a learner requires voter reconfiguration.", nameof(options));
        }
        if (_options.AutomaticVoterReplacementDelay is { } replacementDelay
            && (!_options.EnableVoterReconfiguration || replacementDelay <= TimeSpan.Zero))
        {
            throw new ArgumentException("Automatic voter replacement requires reconfiguration and a positive delay.", nameof(options));
        }
        _bootstrapFromMembership = _options.VoterIds is { Length: 0 } && _expectedVoterCount > 0;
        if (!_bootstrapFromMembership)
        {
            _voters = new RaftVoterConfiguration(_options.VoterIds, _expectedVoterCount,
                _clusterMembership.SelfMember.Node.Id, _options.JoinAsLearner);
            _bootstrapComplete = true;
        }

        _logSerializer = (ISerializer)_serviceProvider.GetRequiredService(_options.LogSerializerType);

        _currentTerm = 0;
        _votedFor = null;

        _leaderState = null;
        _candidateState = null;
        _followerState = null;
        Status = RaftNodeStatus.Unknown;
    }

    protected override async Task OnStarting()
    {
        await base.OnStarting();
        if (_options.EnableVoterReconfiguration && _logRepository is not IDurableRaftLogRepository)
        {
            throw new InvalidOperationException("Raft voter reconfiguration requires a durable log repository.");
        }

        if (_bootstrapFromMembership)
        {
            if (string.IsNullOrWhiteSpace(_clusterMembership.ClusterId))
            {
                throw new InvalidOperationException("Raft bootstrap requires a cluster ID.");
            }

            _bootstrapStore = _serviceProvider.GetRequiredService<IRaftBootstrapStore>();
        }

        if (_bootstrapFromMembership || _options.EnableVoterReconfiguration)
        {
            await _serviceProvider.GetRequiredService<IClusterPersistenceService>().Restore(CancellationToken.None);
            if (_voters == null && (_currentTerm != 0 || _logRepository.LastIndex.Index != 0))
            {
                throw new InvalidOperationException("Use explicit voter IDs when upgrading an existing Raft cluster.");
            }
        }

        if (_options.EnableVoterReconfiguration)
        {
            if (_logRepository.FirstAvailableIndex != 1)
            {
                throw new InvalidOperationException("Raft voter reconfiguration requires retaining the full log.");
            }
            if (_logRepository.LastIndex.Index > 0 && (!_restoredPersistentState || _logRepository.LastIndex.Term > _currentTerm))
            {
                throw new InvalidOperationException("Restore the persisted Raft term and vote before opening its durable log.");
            }
            await _stateMachine.Restore();
            await RestoreCommittedCommands();
            await InitializeVoterState();
        }
    }

    private async Task RestoreCommittedCommands()
    {
        var current = _stateMachine.CurrentIndex;
        var committed = _logRepository.CommitedIndex;
        if (current > _logRepository.LastIndex.Index)
        {
            throw new InvalidOperationException("The restored state machine is ahead of the durable Raft log.");
        }
        if (current < committed)
        {
            var entries = await _logRepository.GetLogsAtIndex(current + 1, committed - current);
            foreach (var entry in entries.Where(entry => entry.Entry.Length != 0 && entry.VoterChange == null))
            {
                await _stateMachine.Apply(_logSerializer.Deserialize(entry.Entry), entry.Index);
            }
        }
    }

    private async Task InitializeVoterState()
    {
        if (_options.EnableVoterReconfiguration && (_bootstrapComplete || _allVotersReady))
        {
            if (_options.JoinAsLearner && Voters.Contains(_clusterMembership.SelfMember.Node.Id))
            {
                throw new InvalidOperationException("An initial Raft voter cannot start as a learner.");
            }
            _voterState = new RaftVoterState(Voters.GetIds());
            await _voterState.ReadLog(_logRepository);
        }
    }

    private bool IsVoter(string id) => _voterState?.Current.Contains(id) ?? Voters.Contains(id);

    private bool HasElectionQuorum(ISet<string> votes)
        => _voterState?.Current.HasQuorum(votes) ?? votes.Count >= Voters.Majority;

    private async Task TryBootstrapVoters(CancellationToken token)
    {
        var store = _bootstrapStore ?? throw new InvalidOperationException("Raft bootstrap store was not initialized.");
        var clusterId = _clusterMembership.ClusterId;
        var selfId = _clusterMembership.SelfMember.Node.Id;
        var discoveredIds = _clusterMembership.OtherMembers
            .Select(member => member.Node.Id)
            .Append(selfId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var proposal = !_options.JoinAsLearner && _voters == null && discoveredIds.Length == _expectedVoterCount
            ? new RaftVoterConfiguration(discoveredIds, _expectedVoterCount, selfId).GetIds()
            : null;
        var publishedIds = await store.GetOrPublishVoters(clusterId, proposal, token);
        if (publishedIds == null || (!_options.JoinAsLearner && !publishedIds.Contains(selfId, StringComparer.Ordinal)))
        {
            return;
        }

        var published = new RaftVoterConfiguration(publishedIds, _expectedVoterCount, selfId, _options.JoinAsLearner);
        if (_options.JoinAsLearner && published.Contains(selfId))
        {
            throw new InvalidOperationException("An initial Raft voter cannot start as a learner.");
        }
        if (_voters == null && await store.AreVotersReady(clusterId, new[] { selfId }, token))
        {
            throw new InvalidOperationException("This Raft voter has already bootstrapped. Restore its local persisted state.");
        }
        if (_voters != null && !_voters.GetIds().SequenceEqual(published.GetIds(), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The published Raft voters differ from the locally persisted voter list.");
        }

        _voters = published;
        if (!_voterReadyReported)
        {
            await PersistPersistentState();
            if (published.Contains(selfId))
            {
                await store.MarkVoterReady(clusterId, selfId, token);
            }
            _voterReadyReported = true;
        }

        if (await store.AreVotersReady(clusterId, published.GetIds(), token))
        {
            _allVotersReady = true;
            await PersistPersistentState();
            await InitializeVoterState();
            _bootstrapComplete = true;
        }
    }

    protected override async Task OnStopping()
    {
        await base.OnStopping();

        // Stop the leader loop.
        await ClearPreviousState();
    }

    public async ValueTask DisposeAsync()
    {
        await Stop().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    protected async Task StartElection()
    {
        _logger.LogInformation("Starting election for term {Term}", _currentTerm + 1);
        Status = RaftNodeStatus.Candidate;
        await ClearPreviousState();

        // advance term
        UpdateTerm(_currentTerm + 1);

        var randomFactor = new Random().NextDouble();
        var randomInterval = _options.ElectionTimeoutMax.Subtract(_options.ElectionTimeoutMin).Multiply(randomFactor);
        _candidateState = new RaftCandidateState(_currentTerm, _time.Now.Add(_options.ElectionTimeoutMin).Add(randomInterval));

        // vote for self
        _votedFor = _clusterMembership.SelfMember.Node.Id;
        _candidateState.AddVote(_votedFor);
        await PersistPersistentState();

        if (HasElectionQuorum(_candidateState.RecivedVotesFrom))
        {
            await BecomeLeader();
            return;
        }

        // request votes from each other node
        var lastIndex = _logRepository.LastIndex;
        var r = new RequestVoteRequest { CandidateId = _clusterMembership.SelfMember.Node.Id, Term = _currentTerm, LastLogIndex = lastIndex.Index, LastLogTerm = lastIndex.Term };
        var tasks = _clusterMembership.OtherMembers
            .Where(member => IsVoter(member.Node.Id) && member.Node.Id != _votedFor)
            .Select(member => _messageSender.SendMessage(r, member.Node.Address));
        await Task.WhenAll(tasks);
    }

    protected async Task BecomeFollower(int term)
    {
        _logger.LogInformation("Becoming a follower for term {Term}", term);
        Status = RaftNodeStatus.Follower;
        await ClearPreviousState();

        if (term > _currentTerm)
        {
            UpdateTerm(term);
        }
        await PersistPersistentState();

        _followerState = new RaftFollowerState(_loggerFactory.CreateLogger<RaftFollowerState>(), _options, _time, _currentTerm, null);
    }

    private async Task ClearPreviousState()
    {
        if (_leaderState != null)
        {
            await _leaderState.Stop();
            _leaderState = null;
        }

        _candidateState = null;

        _followerState = null;

        ClearInstallingSnapshot();
    }

    private void ClearInstallingSnapshot()
    {
        _installingSnapshot?.Dispose();
        _installingSnapshot = null;
        _installingSnapshotId = null;
        _installingSnapshotLeaderId = null;
        _installingSnapshotLastIncludedIndex = 0;
        _installingSnapshotLastIncludedTerm = 0;
    }

    protected async Task BecomeLeader()
    {
        _logger.LogInformation("Becoming a leader {Node} for term {Term}", _clusterMembership.SelfMember.Node, _currentTerm);
        Status = RaftNodeStatus.Leader;

        await ClearPreviousState();

        _leaderState = new RaftLeaderState(
            _loggerFactory.CreateLogger<RaftLeaderState>(),
            CurrentTerm,
            _options,
            _clusterMembership,
            _messageSender,
            _logRepository,
            _stateMachine,
            _logSerializer,
            _time,
            ObserveHigherTerm,
            Voters,
            _voterState);

        await _leaderState.Start();
    }

    protected async Task OnRequestVoteRequest(RequestVoteRequest r, INode node)
    {
        _logger.LogTrace("{Node}: VotedFor = {VotedFor}, CurrentTerm = {Term}, LogLastIndex = {LogLastIndex}", node, _votedFor, _currentTerm, _logRepository.LastIndex);

        // if higher term discoverd
        if (r.Term > _currentTerm)
        {
            await OnNewerTermDiscovered(r.Term, node);
        }

        var resp = new RequestVoteResponse(r) { VoteGranted = false };
        if (r.Term < _currentTerm)
        {
            // Tell sender to update iself to a higher term.
            resp.Term = _currentTerm;
        }
        else
        {
            resp.Term = r.Term;
            if (IsVoter(_clusterMembership.SelfMember.Node.Id)
                && r.CandidateId == node.Id && (_votedFor == null || _votedFor == node.Id))
            {
                // Raft §5.4.1: candidate log is up-to-date if its last term is higher,
                // or same term but index is at least as long as ours.
                var ourLastIndex = _logRepository.LastIndex;
                var logIsUpToDate = r.LastLogTerm > ourLastIndex.Term
                    || (r.LastLogTerm == ourLastIndex.Term && r.LastLogIndex >= ourLastIndex.Index);
                if (logIsUpToDate)
                {
                    // Grant vote - sender's log is at least as up to date as this node's.
                    resp.VoteGranted = true;
                    // Save who we given the vote to
                    _votedFor = node.Id;
                    await PersistPersistentState();
                    _followerState?.OnLeaderMessage(_followerState.Leader);
                }
            }
        }

        _logger.LogInformation("Sending {Message} to node {Node}", resp, node);
        await _messageSender.SendMessage(resp, node.Address);
    }

    protected async Task OnRequestVoteResponse(RequestVoteResponse r, INode node)
    {
        // if higher term discoverd
        if (r.Term > _currentTerm)
        {
            await OnNewerTermDiscovered(r.Term, node);
            return;
        }

        // if still a candidate
        if (Status != RaftNodeStatus.Candidate)
        {
            _logger.LogDebug("Node {Node} sent {MessageType}, but this node is in {NodeStatus} status already", node, r.GetType().Name, Status);
            return;
        }

        // if still on current term 
        if (r.Term == _currentTerm)
        {
            _logger.LogInformation("Node {Node} granted vote {VoteGranted} in term {Term}", node, r.VoteGranted, _currentTerm);
            if (r.VoteGranted)
            {
                _candidateState!.AddVote(node.Id);

                if (HasElectionQuorum(_candidateState.RecivedVotesFrom))
                {
                    _logger.LogInformation("Received {VoteCount} votes in term {Term}", _candidateState.RecivedVotesFrom.Count, _currentTerm);
                    await BecomeLeader();
                }
            }
        }
    }

    private Task ObserveHigherTerm(int term, INode node)
    {
        // The node loop owns role changes. The leader loop must not wait for itself to stop.
        _higherTerms.Enqueue((term, node));
        return Task.CompletedTask;
    }

    internal Task OnNewerTermDiscovered(int term, INode node)
    {
        if (term <= _currentTerm)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("{Node}: Node indicated there is a higher term {Term}", node, term);
        return BecomeFollower(term);
    }

    private Task SendAppendEntriesResponse(AppendEntriesRequest r, INode node, bool success)
    {
        var resp = new AppendEntriesResponse(r) { Term = _currentTerm, Success = success };
        return _messageSender.SendMessage(resp, node.Address);
    }

    private Task SendInstallSnapshotResponse(InstallSnapshotRequest r, INode node, bool success)
    {
        var resp = new InstallSnapshotResponse(r) { Term = _currentTerm, Success = success };
        return _messageSender.SendMessage(resp, node.Address);
    }

    protected async Task OnAppendEntriesRequest(AppendEntriesRequest r, INode node)
    {
        _logger.LogTrace("Handling {Message} from {Node}", r, node);

        // If higher term discoverd
        if (r.Term > _currentTerm)
        {
            await OnNewerTermDiscovered(r.Term, node);
        }

        if (r.Term < _currentTerm)
        {
            // Tell the leader there is a new term
            await SendAppendEntriesResponse(r, node, success: false);
            return;
        }

        if (Status != RaftNodeStatus.Follower)
        {
            // Become follower immediately
            await BecomeFollower(r.Term);
        }
        _followerState?.OnLeaderMessage(node);

        if (r.PrevLogIndex < _logRepository.LastCompactedIndex.Index
            || _logRepository.LastIndex.Index < r.PrevLogIndex
            || r.PrevLogIndex == 0 && r.PrevLogTerm != 0
            || r.PrevLogIndex > 0 && _logRepository.GetTermAtIndex(r.PrevLogIndex) != r.PrevLogTerm)
        {
            // Does not contain that log entry yet
            // OR the term at the given index does not match what's in the log entry
            await SendAppendEntriesResponse(r, node, success: false);
            return;
        }

        var lastVerifiedIndex = r.PrevLogIndex;
        if (r.Entries != null && r.Entries.Count > 0)
        {
            foreach (var entry in r.Entries)
            {
                if (entry.Index != lastVerifiedIndex + 1)
                {
                    await SendAppendEntriesResponse(r, node, success: false);
                    return;
                }
                if (entry.VoterChange != null && (_voterState == null || entry.Entry.Length != 0))
                {
                    await SendAppendEntriesResponse(r, node, success: false);
                    return;
                }
                lastVerifiedIndex = entry.Index;
            }

            try
            {
                await _logRepository.Append(r.Entries);
                if (_voterState != null)
                {
                    await _voterState.ReadLog(_logRepository);
                }
            }
            catch (InvalidOperationException e)
            {
                _logger.LogWarning(e, "Rejecting {Message} because it conflicts with committed log entries", r);
                await SendAppendEntriesResponse(r, node, success: false);
                return;
            }
        }

        // Confirm all was good
        await SendAppendEntriesResponse(r, node, success: true);

        // Apply logs in the local state machine if leader has a higher commit index
        var commitedIndex = _logRepository.CommitedIndex;
        var newCommitIndex = Math.Min(r.LeaderCommitIndex, lastVerifiedIndex);
        if (newCommitIndex > commitedIndex)
        {
            var indexStart = commitedIndex + 1;
            var logsCount = newCommitIndex - commitedIndex;
            var logs = await _logRepository.GetLogsAtIndex(indexStart, logsCount).ConfigureAwait(false);
            for (var i = 0; i < logsCount; i++)
            {
                await _stateMachine.Apply(_logRepository, logEntry: logs[i].Entry, logIndex: indexStart + i, _logger, _logSerializer);
            }
        }
    }

    protected async Task OnInstallSnapshotRequest(InstallSnapshotRequest r, INode node)
    {
        _logger.LogTrace("Handling {Message} from {Node}", r, node);

        if (r.Term > _currentTerm)
        {
            await OnNewerTermDiscovered(r.Term, node);
        }

        if (r.Term < _currentTerm)
        {
            await SendInstallSnapshotResponse(r, node, success: false);
            return;
        }

        if (Status != RaftNodeStatus.Follower)
        {
            await BecomeFollower(r.Term);
        }
        _followerState?.OnLeaderMessage(node);

        if (r.LastIncludedIndex <= _logRepository.CommitedIndex)
        {
            await SendInstallSnapshotResponse(r, node, success: true);
            return;
        }

        if (r.Offset == 0)
        {
            ClearInstallingSnapshot();
            _installingSnapshot = new MemoryStream();
            _installingSnapshotId = r.SnapshotId;
            _installingSnapshotLeaderId = r.LeaderId;
            _installingSnapshotLastIncludedIndex = r.LastIncludedIndex;
            _installingSnapshotLastIncludedTerm = r.LastIncludedTerm;
        }

        var dataLength = r.Data?.Length ?? 0;
        if (_installingSnapshot == null
            || _installingSnapshotId != r.SnapshotId
            || _installingSnapshotLeaderId != r.LeaderId
            || _installingSnapshotLastIncludedIndex != r.LastIncludedIndex
            || _installingSnapshotLastIncludedTerm != r.LastIncludedTerm
            || _installingSnapshot.Length != r.Offset
            || _installingSnapshot.Length + dataLength > _options.MaxInstallSnapshotBytes)
        {
            await SendInstallSnapshotResponse(r, node, success: false);
            return;
        }

        if (r.Data != null)
        {
            await _installingSnapshot.WriteAsync(r.Data);
        }

        if (r.Done)
        {
            var snapshotBytes = _installingSnapshot.ToArray();
            ClearInstallingSnapshot();

            await _stateMachine.InstallSnapshot(snapshotBytes, r.LastIncludedIndex, r.LastIncludedTerm);
            await _logRepository.InstallSnapshot(new LogIndex(r.LastIncludedIndex, r.LastIncludedTerm));
        }

        await SendInstallSnapshotResponse(r, node, success: true);
    }

    private void UpdateTerm(int term)
    {
        _currentTerm = term;
        _votedFor = null;
    }

    private async Task PersistPersistentState()
    {
        var persistenceService = _serviceProvider.GetService<IClusterPersistenceService>();
        if ((_bootstrapFromMembership || _options.EnableVoterReconfiguration) && persistenceService == null)
        {
            throw new InvalidOperationException("Raft bootstrap and voter reconfiguration require local persistence.");
        }
        if (persistenceService != null)
        {
            _persistentStateDirty = true;
            await persistenceService.Persist(CancellationToken.None);
            _persistentStateDirty = false;
        }
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        if (!_bootstrapComplete)
        {
            await TryBootstrapVoters(token);
            return true;
        }

        if (_persistentStateDirty)
        {
            await PersistPersistentState();
        }

        var selfId = _clusterMembership.SelfMember.Node.Id;
        if (_leaderState?.Retired == true)
        {
            await BecomeFollower(_currentTerm);
        }

        var idleRun = true;

        while (_higherTerms.TryDequeue(out var observed))
        {
            await OnNewerTermDiscovered(observed.Term, observed.Node);
            idleRun = false;
        }

        if (_messages.TryDequeue(out var arrivedMessage))
        {
            // ToDo: Check if message already expires, if so skip

            var address = arrivedMessage.Address;
            var node = _clusterMembership.OtherMembers.FirstOrDefault(x => x.Node.Address.Equals(address))?.Node;
            if (node != null && node.Id != selfId)
            {
                _logger.LogDebug("Recieved {MessageType} from node {Node}", arrivedMessage.Message.GetType().Name, node);

                var task = arrivedMessage.Message switch
                {
                    RequestVoteRequest r when IsVoter(node.Id) => OnRequestVoteRequest(r, node),
                    RequestVoteResponse r when IsVoter(node.Id) => OnRequestVoteResponse(r, node),
                    AppendEntriesRequest r when r.LeaderId == node.Id && (_voterState != null || IsVoter(node.Id)) => OnAppendEntriesRequest(r, node),
                    InstallSnapshotRequest r when _voterState == null && r.LeaderId == node.Id && IsVoter(node.Id) => OnInstallSnapshotRequest(r, node),
                    LearnerProbeRequest r when _voterState != null => _messageSender.SendMessage(
                        new LearnerProbeResponse(r) { IsStandbyLearner = _options.JoinAsLearner && !_voterState.HasEverVoted(selfId) }, node.Address),
                    _ => null
                };
                if (task != null)
                {
                    await task;
                    idleRun = false;
                }
            }
            else
            {
                _logger.LogDebug("Ignoring Raft message from {NodeAddress}: no matching remote voter", address);
            }
        }

        if (Status == RaftNodeStatus.Unknown)
        {
            await BecomeFollower(_currentTerm);
            idleRun = false;
        }

        if (Status == RaftNodeStatus.Candidate)
        {
            // When election timeout, start new election.
            if (_time.Now > _candidateState!.ElectionTimeout)
            {
                _logger.LogInformation("Did not reach consensus on a leader for term {Term} within the alloted timeout {ElectionTimeout} - starting another election", _currentTerm, _options.ElectionTimeoutMin);
                await StartElection();
                idleRun = false;
            }
        }

        if (Status == RaftNodeStatus.Follower && _followerState != null && IsVoter(selfId))
        {
            // When election timeout, start new election.
            // ToDo: Allow for a longer window here on node start esp when the leader is already established - wait for membership for a bit
            if (_time.Now > _followerState.LeaderTimeout)
            {
                _logger.LogInformation("Did not hear from leader within the alloted timeout {LeaderTimeout} - starting an election", _options.LeaderTimeout);
                await StartElection();
                idleRun = false;
            }
        }

        return idleRun;
    }

    public Task OnMessageArrived(object message, IAddress address)
    {
        if (_bootstrapComplete && message is RaftMessage raftMessage)
        {
            _messages.Enqueue((raftMessage, address));
        }
        return Task.CompletedTask;
    }

    public bool CanHandle(object message) => message is RaftMessage;

    #region IDurableComponent

    public void OnStateRestore(IStateReader state)
    {
        _logger.LogInformation("Restoring state");

        if (_bootstrapFromMembership)
        {
            var ids = state.Get<string[]>("bootstrapVoterIds");
            if (ids != null && state.Get<string>("bootstrapClusterId") != _clusterMembership.ClusterId)
            {
                throw new InvalidOperationException("The persisted Raft voter list belongs to a different cluster ID.");
            }
            _voters = ids == null ? null : new RaftVoterConfiguration(ids, _expectedVoterCount, _clusterMembership.SelfMember.Node.Id, _options.JoinAsLearner);
            _allVotersReady = _voters != null && state.Get<bool>("bootstrapComplete");
            _bootstrapComplete = _allVotersReady;
            _voterReadyReported = false;
        }
        else if (_options.EnableVoterReconfiguration && state.Get<string[]>("initialVoterIds") is { } initialIds
            && (!Voters.GetIds().SequenceEqual(initialIds, StringComparer.Ordinal)
                || state.Get<string>("reconfigurationClusterId") != _clusterMembership.ClusterId))
        {
            throw new InvalidOperationException("The persisted Raft state belongs to a different cluster ID or initial voter list.");
        }

        _votedFor = state.Get<string?>("votedFor");
        _currentTerm = state.Get<int>("currentTerm");
        _restoredPersistentState = true;
        var restoredStatus = RaftNodeStatus.FromId(state.Get<Guid>("statusId"));
        if (restoredStatus == RaftNodeStatus.Leader)
        {
            // Older versions cleared the self-vote when becoming leader.
            _votedFor ??= _clusterMembership.SelfMember.Node.Id;
        }

        ClearPreviousState().GetAwaiter().GetResult();
        if (restoredStatus == RaftNodeStatus.Unknown)
        {
            Status = RaftNodeStatus.Unknown;
        }
        else
        {
            Status = RaftNodeStatus.Follower;
            _followerState = new RaftFollowerState(_loggerFactory.CreateLogger<RaftFollowerState>(), _options, _time, _currentTerm, null);
        }
    }

    public void OnStatePersist(IStateWriter state)
    {
        _logger.LogInformation("Persisting state");

        state.Set("votedFor", _votedFor);
        state.Set("currentTerm", _currentTerm);
        state.Set("statusId", Status.Id);
        if (_bootstrapFromMembership)
        {
            state.Set("bootstrapClusterId", _clusterMembership.ClusterId);
            state.Set("bootstrapVoterIds", _voters?.GetIds());
            state.Set("bootstrapComplete", _allVotersReady);
        }
        else if (_options.EnableVoterReconfiguration)
        {
            state.Set("reconfigurationClusterId", _clusterMembership.ClusterId);
            state.Set("initialVoterIds", Voters.GetIds());
        }

        if (_leaderState != null && !_options.EnableVoterReconfiguration)
        {
            var leaderState = state.SubComponent("Leader");
            _leaderState.OnStatePersist(leaderState);
        }
    }

    public Task<object?> OnClientRequest(object command, CancellationToken token)
    {
        var leaderState = _leaderState;
        if (leaderState == null || Status != RaftNodeStatus.Leader)
        {
            throw new ClusterException($"The current node is not the leader, so it cannot handle client requests. Last known leader node: {_followerState?.Leader}");
        }
        return leaderState.OnClientRequest(command, token);
    }

    public Task ChangeVoters(IReadOnlyCollection<string> voterIds, CancellationToken token = default)
    {
        var leader = _leaderState;
        if (_voterState == null || leader == null || Status != RaftNodeStatus.Leader)
        {
            throw new InvalidOperationException("Voter changes require a leader with Raft voter reconfiguration enabled.");
        }
        return leader.ChangeVoters(voterIds, token);
    }

    #endregion
}
