namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Transport;

public class RaftLeaderStateTests : AbstractRaftIntegrationTest, IAsyncLifetime
{
    private readonly int _term;
    private readonly SerializerMock _logSerializerMock;
    private readonly Mock<OnNewerTermDiscovered> _onNewerTermDiscovered;

    private readonly RaftLeaderState _subject;

    public RaftLeaderStateTests(ITestOutputHelper testOutputHelper)
    {
        _term = 1;
        _logSerializerMock = new();
        _onNewerTermDiscovered = new Mock<OnNewerTermDiscovered>();

        _subject = new RaftLeaderState(
            XUnitLogger.CreateLogger<RaftLeaderState>(testOutputHelper),
            _term,
            _options,
            _clusterMembershipMock.Object,
            _messageSenderMock.Object,
            _logRepositoryMock.Object,
            _stateMachineMock.Object,
            _logSerializerMock.Object,
            _timeMock.Object,
            _onNewerTermDiscovered.Object);

        _options.LeaderPingInterval = TimeSpan.FromSeconds(0.5);
    }

    public Task DisposeAsync() => _subject.Stop();
    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task When_ClientRequest_Then_LogAddedToRepository_And_ReplicatesLogAcrossOtherNodes_And_AppliesToStateMachineWhenReplicatedToMajority_And_Replies()
    {
        // arrange
        var command = _fixture.Create<object>();
        var commandPayload = _fixture.Create<byte[]>();
        var commandResult = _fixture.Create<object>();
        var commandIndex = 2;

        _logSerializerMock.SetupSerDes(command, commandPayload);

        _stateMachineMock
            .Setup(x => x.Apply(command, commandIndex))
            .ReturnsAsync(commandResult);

        var otherNodeReplicatedIndex = _otherMembers.ToDictionary(x => x.Node.Address, x => 0);
        var followersWithCommit = new HashSet<IAddress>();
        var noOpReplicated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitAdvertised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _messageSenderMock
            .Setup(x => x.SendRequest(It.IsAny<AppendEntriesRequest>(), It.IsAny<IAddress>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((IRequest<AppendEntriesResponse> r, IAddress a, TimeSpan? timeout) =>
            {
                var req = (AppendEntriesRequest)r;
                var currentIndex = otherNodeReplicatedIndex[a];
                var success = currentIndex >= req.PrevLogIndex;
                if (success)
                {
                    currentIndex = Math.Max(currentIndex, req.PrevLogIndex + (req.Entries?.Count ?? 0));
                    otherNodeReplicatedIndex[a] = currentIndex;
                    if (otherNodeReplicatedIndex.Values.All(index => index >= 1))
                    {
                        noOpReplicated.TrySetResult();
                    }
                    if (req.LeaderCommitIndex == commandIndex && req.PrevLogIndex == commandIndex && req.Entries == null)
                    {
                        followersWithCommit.Add(a);
                        if (followersWithCommit.Count == _otherMembers.Count)
                        {
                            commitAdvertised.TrySetResult();
                        }
                    }
                }

                return new AppendEntriesResponse((RaftMessage)r) { Success = success, Term = _term };
            });

        await _subject.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await noOpReplicated.Task.WaitAsync(cts.Token);

        // act
        var result = await _subject.OnClientRequest(command, cts.Token);

        _now += _options.LeaderPingInterval;
        await commitAdvertised.Task.WaitAsync(cts.Token);
        await _subject.Stop();

        // assert
        _logRepositoryMock
            .Verify(x => x.Append(_term, Array.Empty<byte>()), Times.Once);

        _logRepositoryMock
            .Verify(x => x.Append(_term, commandPayload), Times.Once);

        _messageSenderMock
            .Verify(x => x.SendRequest(
                It.Is<AppendEntriesRequest>(r => r.PrevLogIndex == 0 && r.PrevLogTerm == 0 && r.Entries != null && r.Entries.Count == 1 && r.Entries.First().Entry.Length == 0),
                It.IsAny<IAddress>(),
                _options.LeaderPingInterval),
                Times.AtLeast(_otherMembers.Count));

        _messageSenderMock
            .Verify(x => x.SendRequest(
                It.Is<AppendEntriesRequest>(r => r.PrevLogIndex == 1 && r.PrevLogTerm == _term && r.Entries != null && r.Entries.Count == 1 && r.Entries.First().Entry == commandPayload),
                It.IsAny<IAddress>(),
                _options.LeaderPingInterval),
                Times.Exactly(_otherMembers.Count));

        _messageSenderMock
            .Verify(x => x.SendRequest(
                It.Is<AppendEntriesRequest>(r => r.PrevLogIndex == 1 && r.PrevLogTerm == _term && r.Entries == null),
                It.IsAny<IAddress>(),
                _options.LeaderPingInterval),
                Times.AtLeast(_otherMembers.Count));

        _messageSenderMock
            .Verify(x => x.SendRequest(
                It.Is<AppendEntriesRequest>(r => r.PrevLogIndex == 2 && r.PrevLogTerm == _term && r.Entries == null),
                It.IsAny<IAddress>(),
                _options.LeaderPingInterval),
                Times.AtLeast(_otherMembers.Count));

        _messageSenderMock
            .VerifyNoOtherCalls();

        _stateMachineMock
            .Verify(x => x.Apply(command, commandIndex), Times.Once);

        result.Should().Be(commandResult);
    }
}
