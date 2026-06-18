namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Serialization;
using SlimCluster.Transport;

/// <summary>
/// Tests for majority-based commit logic in RaftLeaderState.
/// Verifies that the leader counts itself in the quorum, so a 3-node cluster
/// commits after replication to just 1 follower (not requiring both).
/// </summary>
public class RaftLeaderMajorityTests : AbstractRaftIntegrationTest, IAsyncLifetime
{
    private readonly int _term = 1;
    private readonly SerializerMock _logSerializerMock;
    private readonly Mock<OnNewerTermDiscovered> _onNewerTermDiscovered;
    private readonly RaftLeaderState _subject;

    public RaftLeaderMajorityTests(ITestOutputHelper testOutputHelper)
    {
        _term = 1;
        _logSerializerMock = new SerializerMock();
        _onNewerTermDiscovered = new Mock<OnNewerTermDiscovered>();

        _options.NodeCount = 3;
        _options.LeaderPingInterval = TimeSpan.FromMilliseconds(200);

        _subject = new RaftLeaderState(
            XUnitLogger.CreateLogger<RaftLeaderState>(testOutputHelper),
            _term,
            _options,
            _clusterMembershipMock.Object,
            _messageSenderMock.Object,
            _logRepositoryMock.Object,
            _stateMachineMock.Object,
            _logSerializerMock.Object,
            new Time(),
            _onNewerTermDiscovered.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _subject.Stop();

    private sealed class RaftLeaderStateProxy : RaftLeaderState
    {
        public RaftLeaderStateProxy(
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
            : base(logger, term, options, clusterMembership, messageSender, logRepository, stateMachine, logSerializer, time, onNewerTermDiscovered)
        {
        }

        public Task<bool> OnLoopRunProxy() => OnLoopRun(default);
    }

    /// <summary>
    /// In a 3-node cluster the leader should commit once ONE follower replicates
    /// (leader + 1 follower = 2 > 3/2 = 1, i.e. majority).
    /// Before the fix this required BOTH followers to replicate (100%).
    /// </summary>
    [Fact]
    public async Task Given_ThreeNodeCluster_When_OneFollowerReplicates_Then_EntryCommitted()
    {
        // arrange
        var command = new object();
        var commandPayload = new byte[] { 42 };
        var commandResult = new object();

        _logSerializerMock.SetupSerDes(command, commandPayload);
        _stateMachineMock
            .Setup(x => x.Apply(command, 1))
            .ReturnsAsync(commandResult);

        // node1 (index 0) replicates successfully; node2 (index 1) never advances past index 0
        var replicatedByNode = new Dictionary<IAddress, int>();
        foreach (var m in _otherMembers) replicatedByNode[m.Node.Address] = 0;

        _messageSenderMock
            .Setup(x => x.SendRequest(It.IsAny<AppendEntriesRequest>(), It.IsAny<IAddress>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((IRequest<AppendEntriesResponse> r, IAddress addr, TimeSpan? _) =>
            {
                var req = (AppendEntriesRequest)r;
                if (addr.Equals(_otherMembers[0].Node.Address))
                {
                    // Follower 0 replicates normally
                    var current = replicatedByNode[addr];
                    var success = current >= req.PrevLogIndex;
                    if (success)
                        replicatedByNode[addr] = Math.Max(current, req.PrevLogIndex + (req.Entries?.Count ?? 0));
                    return new AppendEntriesResponse((RaftMessage)r) { Success = success, Term = _term };
                }
                // Follower 1: always succeeds for heartbeats (no entries) but never advances past 0
                // This prevents NextIndex from decrementing below 1
                var hasEntries = req.Entries?.Count > 0;
                return new AppendEntriesResponse((RaftMessage)r) { Success = !hasEntries, Term = _term };
            });

        await _subject.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(500)); // let heartbeats establish MatchIndex=0

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // act: submit a command — should complete once follower[0] confirms
        var result = await _subject.OnClientRequest(command, cts.Token);

        // assert: result returned (state machine applied), meaning commit happened with just 1 follower
        result.Should().Be(commandResult);
        _stateMachineMock.Verify(x => x.Apply(command, 1), Times.Once);
    }

    /// <summary>
    /// Commit must NOT happen when zero followers have replicated (only leader has the entry).
    /// With NodeCount=3, majority=2, leader alone is only 1 node.
    /// </summary>
    [Fact]
    public async Task Given_ThreeNodeCluster_When_NoFollowerReplicates_Then_EntryNotCommitted()
    {
        // arrange
        var command = new object();
        var commandPayload = new byte[] { 99 };

        _logSerializerMock.SetupSerDes(command, commandPayload);

        // All followers always reject
        _messageSenderMock
            .Setup(x => x.SendRequest(It.IsAny<AppendEntriesRequest>(), It.IsAny<IAddress>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((IRequest<AppendEntriesResponse> r, IAddress _, TimeSpan? __) =>
                new AppendEntriesResponse((RaftMessage)r) { Success = false, Term = _term });

        await _subject.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

        // act: command should time out because no follower replicates
        var act = () => _subject.OnClientRequest(command, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // assert: state machine was never applied
        _stateMachineMock.Verify(x => x.Apply(It.IsAny<object>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Given_MajorityReplicatedEntryFromOlderTerm_When_LeaderAppliesLogs_Then_EntryNotCommitted()
    {
        // arrange
        var leaderTerm = 2;
        var previousTerm = 1;
        var command = new object();
        var commandPayload = new byte[] { 11 };

        _logSerializerMock.SetupSerDes(command, commandPayload);
        await _logRepositoryMock.Object.Append(previousTerm, commandPayload);

        var subject = CreateLeaderStateProxy(leaderTerm);
        PreventReplicationRequests(subject, matchIndexForFirstFollower: 1, matchIndexForSecondFollower: 0);

        // act
        await subject.OnLoopRunProxy();

        // assert
        _logRepositoryMock.Object.CommitedIndex.Should().Be(0);
        _stateMachineMock.Verify(x => x.Apply(It.IsAny<object>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Given_MajorityReplicatedEntryFromCurrentTerm_When_LeaderAppliesLogs_Then_EntryCommitted()
    {
        // arrange
        var leaderTerm = 2;
        var command = new object();
        var commandPayload = new byte[] { 12 };
        var commandResult = new object();

        _logSerializerMock.SetupSerDes(command, commandPayload);
        _stateMachineMock.Setup(x => x.Apply(command, 1)).ReturnsAsync(commandResult);
        await _logRepositoryMock.Object.Append(leaderTerm, commandPayload);

        var subject = CreateLeaderStateProxy(leaderTerm);
        PreventReplicationRequests(subject, matchIndexForFirstFollower: 1, matchIndexForSecondFollower: 0);

        // act
        await subject.OnLoopRunProxy();

        // assert
        _logRepositoryMock.Object.CommitedIndex.Should().Be(1);
        _stateMachineMock.Verify(x => x.Apply(command, 1), Times.Once);
    }

    private RaftLeaderStateProxy CreateLeaderStateProxy(int term)
    {
        return new RaftLeaderStateProxy(
            NullLogger<RaftLeaderState>.Instance,
            term,
            _options,
            _clusterMembershipMock.Object,
            _messageSenderMock.Object,
            _logRepositoryMock.Object,
            _stateMachineMock.Object,
            _logSerializerMock.Object,
            new Time(),
            _onNewerTermDiscovered.Object);
    }

    private void PreventReplicationRequests(RaftLeaderState subject, int matchIndexForFirstFollower, int matchIndexForSecondFollower)
    {
        var recentlySent = DateTimeOffset.UtcNow.AddDays(1);

        subject.ReplicationStateByNode[_otherMembers[0].Node.Id] = new FollowerReplicatonState
        {
            NextIndex = 2,
            MatchIndex = matchIndexForFirstFollower,
            LastAppendRequest = recentlySent
        };
        subject.ReplicationStateByNode[_otherMembers[1].Node.Id] = new FollowerReplicatonState
        {
            NextIndex = 2,
            MatchIndex = matchIndexForSecondFollower,
            LastAppendRequest = recentlySent
        };
    }
}
