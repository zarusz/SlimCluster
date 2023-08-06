namespace SlimCluster.Consensus.Raft;

using System.Collections.Concurrent;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Host;
using SlimCluster.Host.Common;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Serialization;

public class RaftNode : TaskLoop, IMessageArrivedHandler, IAsyncDisposable, IDurableComponent, IClusterControlComponent, IRaftClientRequestHandler
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IClusterMembership _clusterMembership;
    private readonly IMessageSender _messageSender;
    private readonly RaftConsensusOptions _options;
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

    private readonly ConcurrentQueue<(RaftMessage Message, IAddress Address)> _messages = new();

    public int CurrentTerm => _currentTerm;

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

        _currentTerm = 0;
        _votedFor = null;

        _leaderState = null;
        _candidateState = null;
        _followerState = null;
        Status = RaftNodeStatus.Unknown;

        if (_options.AutoStart)
        {
            _ = Start();
        }
    }

    protected override async Task OnStopping()
    {
        // Stop the leader loop.
        if (_leaderState != null)
        {
            await _leaderState.Stop();
            _leaderState = null;
        }
        await base.OnStopping();
    }

    public async ValueTask DisposeAsync()
    {
        await Stop().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    protected async Task StartElection()
    {
        Status = RaftNodeStatus.Candidate;

        // advance term
        UpdateTerm(_currentTerm + 1);

        var randomFactor = new Random().NextDouble();
        var randomInterval = _options.ElectionTimeoutMax.Subtract(_options.ElectionTimeoutMin).Multiply(randomFactor);
        _candidateState = new RaftCandidateState(_currentTerm, _time.Now.Add(_options.ElectionTimeoutMin).Add(randomInterval));
        _logger.LogInformation("Starting election for term {Term}", _currentTerm);

        // vote for self
        _votedFor = _clusterMembership.SelfMember.Node.Id;
        _candidateState.AddVote(_votedFor);

        // request votes from each other node
        var lastIndex = _logRepository.LastIndex;
        var r = new RequestVoteRequest { CandidateId = _clusterMembership.SelfMember.Node.Id, Term = _currentTerm, LastLogIndex = lastIndex.Index, LastLogTerm = lastIndex.Term };
        var tasks = _clusterMembership.OtherMembers.Select(member => _messageSender.SendMessage(r, member.Node.Address));
        await Task.WhenAll(tasks);
    }

    protected async Task BecomeFollower(int term)
    {
        _logger.LogInformation("Becoming a follower for term {Term}", term);
        Status = RaftNodeStatus.Follower;

        UpdateTerm(term);

        if (_leaderState != null)
        {
            await _leaderState.Stop();
            _leaderState = null;
        }

        _candidateState = null;

        _followerState = new RaftFollowerState(_loggerFactory.CreateLogger<RaftFollowerState>(), _options, _time, null);
    }

    protected async Task BecomeLeader()
    {
        _logger.LogInformation("Becoming a leader {Node} for term {Term}", _clusterMembership.SelfMember.Node, _currentTerm);
        Status = RaftNodeStatus.Leader;

        _followerState = null;

        var logSerializer = (ISerializer)_serviceProvider.GetService(_options.LogSerializerType);

        _leaderState = new RaftLeaderState(
            _loggerFactory.CreateLogger<RaftLeaderState>(),
            CurrentTerm,
            _options,
            _clusterMembership,
            _messageSender,
            _logRepository,
            _stateMachine,
            logSerializer,
            _time,
            OnNewerTermDiscovered);

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
            if (_votedFor == null || _votedFor == node.Id)
            {
                if (_logRepository.LastIndex.Index <= r.LastLogIndex)
                {
                    // Grant vote - sender's logs are at least as up to date as this nodes.
                    resp.VoteGranted = true;
                    // Save who we given the vote to
                    _votedFor = node.Id;
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

                // If majority votes, then claim leadership
                if (_candidateState!.RecivedVotesFrom.Count > _options.NodeCount / 2)
                {
                    _logger.LogInformation("Recieved majority of votes {VoteCount} from cluster of {NodeCount} in term {Term}", _candidateState!.RecivedVotesFrom.Count, _options.NodeCount, _currentTerm);

                    // clear who was voted for
                    _votedFor = null;

                    await BecomeLeader();
                }
            }
        }
    }

    internal async Task OnNewerTermDiscovered(int term, INode node)
    {
        // become follower immediately
        _logger.LogInformation("{Node}: Node indicated there is a higher term {Term}", node, term);
        await BecomeFollower(term);
    }

    private Task SendAppendEntriesResponse(AppendEntriesRequest r, INode node, bool success)
    {
        var resp = new AppendEntriesResponse(r) { Term = _currentTerm, Success = success };
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
            // Become followe immediately
            await BecomeFollower(r.Term);
        }
        _followerState?.OnLeaderMessage(node);

        if (_logRepository.LastIndex.Index < r.PrevLogIndex
            || (r.PrevLogIndex == 0 && r.PrevLogTerm != 0 || r.PrevLogIndex > 0 && _logRepository.GetTermAtIndex(r.PrevLogIndex) != r.PrevLogTerm))
        {
            // Does not contain that log entry yet
            // OR the term at the given index does not match what's in the log entry
            await SendAppendEntriesResponse(r, node, success: false);
            return;
        }

        if (r.Entries != null && r.Entries.Count > 0)
        {
            await _logRepository.Append(r.PrevLogIndex + 1, r.Term, r.Entries);
        }

        // Confirm all was good
        await SendAppendEntriesResponse(r, node, success: true);

        // Apply logs in the local state machine if leader has a higher commit index
        var commitedIndex = _logRepository.CommitedIndex;
        if (r.LeaderCommitIndex > commitedIndex)
        {
            var indexStart = commitedIndex + 1;
            var newCommitIndex = Math.Min(r.LeaderCommitIndex, _logRepository.LastIndex.Index);
            var logsCount = newCommitIndex - commitedIndex;
            var logs = await _logRepository.GetLogsAtIndex(indexStart, logsCount).ConfigureAwait(false);
            for (var i = 0; i < logsCount; i++)
            {
                var commandIndex = indexStart + i;
                await _stateMachine.Apply(command: logs[i], index: commandIndex).ConfigureAwait(false);
                await _logRepository.Commit(commandIndex).ConfigureAwait(false);
            }
        }
    }

    private void UpdateTerm(int term)
    {
        _currentTerm = term;
        _votedFor = null;
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        var idleRun = true;

        if (_messages.TryDequeue(out var arrivedMessage))
        {
            var address = arrivedMessage.Address;
            var node = _clusterMembership.OtherMembers.FirstOrDefault(x => x.Node.Address.Equals(address))?.Node;
            if (node != null)
            {
                _logger.LogDebug("Recieved {MessageType} from node {Node}", arrivedMessage.Message.GetType().Name, node);

                var task = arrivedMessage.Message switch
                {
                    RequestVoteRequest r => OnRequestVoteRequest(r, node),
                    RequestVoteResponse r => OnRequestVoteResponse(r, node),
                    AppendEntriesRequest r => OnAppendEntriesRequest(r, node),
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
                _logger.LogWarning("Could not match Node based on address {NodeAddress} from the memberlist", address);
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

        if (Status == RaftNodeStatus.Follower && _followerState != null)
        {
            // When election timeout, start new election.
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
        if (message is RaftMessage raftMessage)
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

        _votedFor = state.Get<string?>("votedFor");
        _currentTerm = state.Get<int>("currentTerm");
        Status = RaftNodeStatus.FromId(state.Get<Guid>("statusId"));

        var leaderState = state.SubComponent("Leader");
        if (leaderState != null)
        {
            _leaderState?.OnStateRestore(leaderState);
        }
    }

    public void OnStatePersist(IStateWriter state)
    {
        _logger.LogInformation("Persisting state");

        state.Set("votedFor", _votedFor);
        state.Set("currentTerm", _currentTerm);
        state.Set("statusId", Status.Id);

        if (_leaderState != null)
        {
            var leaderState = state.SubComponent("Leader");
            _leaderState.OnStatePersist(leaderState);
        }
    }

    public Task<object?> OnClientRequest(object command, CancellationToken token)
    {
        var leaderState = _leaderState;
        if (leaderState == null)
        {
            throw new ClusterException($"The current node is not the leader, so it cannot handle client requests. Last known leader node: {_followerState?.Leader}");
        }
        return leaderState.OnClientRequest(command, token);
    }

    #endregion
}
