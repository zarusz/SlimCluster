namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Transport;
using SlimCluster.Transport.Ip;

public class RaftNodeProxy : RaftNode
{
    public RaftNodeProxy(ILoggerFactory loggerFactory, IServiceProvider serviceProvider, IClusterMembership clusterMembership, ITime time, ILogRepository logRepository, IMessageSender messageSender, IStateMachine stateMachine, IOptions<RaftConsensusOptions> options)
        : base(loggerFactory, serviceProvider, clusterMembership, time, logRepository, messageSender, stateMachine, options)
    {
    }

    internal Task<bool> OnLoopRunProxy() => OnLoopRun(default);
}

public class Node : AbstractNode
{
    private IAddress _address;

    public Node(string id, string address, int port) : base(id)
    {
        _address = IPEndPointAddress.Parse($"{address}:{port}");
    }

    public override IAddress Address { get => _address; protected set => _address = value; }

    public override INodeStatus Status => throw new NotImplementedException();
}

public class RaftNodeTests : AbstractRaftIntegrationTest, IAsyncLifetime
{
    private readonly RaftNodeProxy _subject;

    public RaftNodeTests()
    {
        _subject = new RaftNodeProxy(NullLoggerFactory.Instance, _serviceProviderMock.Object, _clusterMembershipMock.Object, _timeMock.Object, _logRepositoryMock.Object, _messageSenderMock.Object, _stateMachineMock.Object, Options.Create(_options));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _subject.DisposeAsync();

    [Fact]
    public async Task Given_New_Then_IsNotStarted()
    {
        // arrange
        await Task.Delay(1000);

        // act

        // assert
        _subject.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task Given_New_When_OnLoopRun_Then_BecomesFollower_And_TermIsZero()
    {
        // arrange

        // act
        var idleRun = await _subject.OnLoopRunProxy();

        // assert
        idleRun.Should().BeFalse();
        _subject.Status.Should().Be(RaftNodeStatus.Follower);
        _subject.CurrentTerm.Should().Be(0);
    }

    [Fact]
    public async Task Given_Follower_When_LeaderTimeout_Then_StartsElection_And_TermIncremented_And_BecomesCandidate()
    {
        // arrange
        await _subject.OnLoopRunProxy();
        var followerTerm = _subject.CurrentTerm;

        // becomes follower

        // act
        _now = _now.Add(_options.LeaderTimeout).AddSeconds(1); // advance time
        var idleRun = await _subject.OnLoopRunProxy();

        // assert
        idleRun.Should().BeFalse(); // loop was not idle

        _subject.Status.Should().Be(RaftNodeStatus.Candidate); // should be candidate
        _subject.CurrentTerm.Should().Be(followerTerm + 1); // should increment the term

        foreach (var member in _otherMembers)
        {
            // verify the other members got a vote request
            _messageSenderMock.Verify(
                x => x.SendMessage(
                    It.Is<RequestVoteRequest>(r => r.CandidateId == _selfMember.Node.Id),
                    member.Node.Address),
                Times.Once);
        }
        _messageSenderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Given_Candidate_When_RecievedAppendEntriesRequestWithHigherTerm_Then_BecomesFollower_And_SwitchToNewTerm()
    {
        // arrange
        await FromFollowerToCandidate();

        var candidateTerm = _subject.CurrentTerm;

        _logRepositoryMock.SetupGet(x => x.LastIndex).Returns(new LogIndex(2, 2));
        _logRepositoryMock.Setup(x => x.GetTermAtIndex(2)).Returns(2);

        var newLeader = _otherMembers[0].Node;
        var newLeaderTerm = candidateTerm + 1;

        // act

        await _subject.OnMessageArrived(
            new AppendEntriesRequest { Term = newLeaderTerm, LeaderId = newLeader.Id, PrevLogIndex = 2, PrevLogTerm = 2 },
            newLeader.Address);

        var idleRun = await _subject.OnLoopRunProxy(); // process the message arrived

        // assert
        idleRun.Should().BeFalse(); // loop was not idle
        _subject.Status.Should().Be(RaftNodeStatus.Follower);
        _subject.CurrentTerm.Should().Be(newLeaderTerm);

        _messageSenderMock.Verify(x => x.SendMessage(It.Is<AppendEntriesResponse>(r => r.Success && r.Term == newLeaderTerm), newLeader.Address), Times.Once);
        _messageSenderMock.Verify(x => x.SendMessage(It.IsAny<RequestVoteRequest>(), It.IsAny<IAddress>()), Times.Exactly(_otherMembers.Count));
        _messageSenderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Given_Candidate_When_RecievedRequestVoteResponse_And_MajorityVotedFor_Then_BecomesLeader(bool majorityNodeGrantedVote)
    {
        // arrange
        await FromFollowerToCandidate();

        var candidateTerm = _subject.CurrentTerm;

        _logRepositoryMock.SetupGet(x => x.LastIndex).Returns(new LogIndex(2, 2));
        _logRepositoryMock.Setup(x => x.GetTermAtIndex(2)).Returns(2);

        var requestVoteRequest = new RequestVoteRequest { Term = candidateTerm, CandidateId = _selfMember.Node.Id, LastLogIndex = 2, LastLogTerm = 2 };

        // act        
        await _subject.OnMessageArrived(
            new RequestVoteResponse(requestVoteRequest) { Term = candidateTerm, VoteGranted = majorityNodeGrantedVote },
            _otherMembers[0].Node.Address);

        var idleRun = await _subject.OnLoopRunProxy(); // process the message arrived

        // assert
        idleRun.Should().BeFalse(); // loop was not idle
        _subject.Status.Should().Be(majorityNodeGrantedVote ? RaftNodeStatus.Leader : RaftNodeStatus.Candidate);
        _subject.CurrentTerm.Should().Be(candidateTerm);

        _messageSenderMock.Verify(x => x.SendMessage(It.IsAny<RequestVoteRequest>(), It.IsAny<IAddress>()), Times.Exactly(_otherMembers.Count));
        if (majorityNodeGrantedVote)
        {
            // wait for the leader loop to start
            await Task.Delay(50);

            foreach (var member in _otherMembers)
            {
                // verify the other members got an append entries request
                _messageSenderMock.Verify(
                    x => x.SendRequest(
                        It.Is<AppendEntriesRequest>(r => r.Term == candidateTerm && r.LeaderId == _selfMember.Node.Id),
                        member.Node.Address,
                        _options.LeaderPingInterval),
                    Times.Once);
            }
        }
        _messageSenderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Given_Candidate_When_ElectionTimeout_Then_StartsNextTermElections()
    {
        // arrange
        await FromFollowerToCandidate();

        var nextTerm = _subject.CurrentTerm + 1;

        // act                
        _now = _now.Add(_options.ElectionTimeoutMax).AddSeconds(1);
        var idleRun = await _subject.OnLoopRunProxy(); // process the message arrived

        // assert
        idleRun.Should().BeFalse(); // loop was not idle
        _subject.Status.Should().Be(RaftNodeStatus.Candidate); // still candidate
        _subject.CurrentTerm.Should().Be(nextTerm); // next term was started

        // request votes for prev term
        _messageSenderMock.Verify(x => x.SendMessage(It.Is<RequestVoteRequest>(r => r.CandidateId == _selfMember.Node.Id && r.Term == nextTerm - 1), It.IsAny<IAddress>()), Times.Exactly(_otherMembers.Count));
        // request votes for next term
        _messageSenderMock.Verify(x => x.SendMessage(It.Is<RequestVoteRequest>(r => r.CandidateId == _selfMember.Node.Id && r.Term == nextTerm), It.IsAny<IAddress>()), Times.Exactly(_otherMembers.Count));
        _messageSenderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, false)]
    [InlineData(1, 3, true)]
    [InlineData(3, 3, true)]
    public async Task Given_Follower_When_RequestVoteRequest_Then_GrantsVote_IfHigherTerm_And_LogAtLeastAsFresh(int candidateTerm, int candidateIndex, bool voteGranted)
    {
        // arrange
        await _subject.OnLoopRunProxy();
        // becomes follower

        var currentIndex = 2;
        var currentTerm = _subject.CurrentTerm;

        _logRepositoryMock.SetupGet(x => x.LastIndex).Returns(new LogIndex(currentIndex, currentTerm));
        _logRepositoryMock.Setup(x => x.GetTermAtIndex(currentIndex)).Returns(currentTerm);

        var candidate = _otherMembers[0].Node;

        // act                
        await _subject.OnMessageArrived(new RequestVoteRequest { CandidateId = candidate.Id, Term = candidateTerm, LastLogIndex = candidateIndex, LastLogTerm = candidateTerm }, _otherMembers[0].Node.Address);
        var idleRun = await _subject.OnLoopRunProxy(); // process the message arrived

        // assert
        idleRun.Should().BeFalse(); // loop was not idl
        _subject.Status.Should().Be(RaftNodeStatus.Follower); // still follower
        _subject.CurrentTerm.Should().Be(Math.Max(candidateTerm, currentTerm)); // next term was started

        // request votes for prev term
        _messageSenderMock.Verify(x => x.SendMessage(It.Is<RequestVoteResponse>(r => r.VoteGranted == voteGranted && r.Term == Math.Max(candidateTerm, currentTerm)), candidate.Address), Times.Once());
        _messageSenderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(2, 1, 1, 1)]
    [InlineData(2, 1, 2, 2)]
    [InlineData(2, 1, 2, 3)]
    public async Task Given_Follower_When_AppendEntriesRequest_Then_AddsToLocalLogs_And_AppliesToStateMachineWhenHigherCommitIndex(int currentIndex, int currentTerm, int newEntriesTerm, int leaderTerm)
    {
        // arrange
        await _subject.OnLoopRunProxy();

        // becomes follower

        var currentCommitIndex = currentIndex;

        // add to existin log entries
        var logRepository = _logRepositoryMock.Object;

        for (var i = 1; i <= currentIndex; i++)
        {
            await logRepository.Append(new[] { new LogEntry(i, currentTerm, _fixture.Create<byte[]>()) });
        }
        if (currentIndex > 0)
        {
            await logRepository.Commit(currentIndex);
        }
        _logRepositoryMock.Reset();

        var leader = _otherMembers[1].Node;

        var newEntry = new LogEntry(currentIndex + 1, newEntriesTerm, _fixture.Create<byte[]>());
        var newEntries = new List<LogEntry> { newEntry };
        // act                
        await _subject.OnMessageArrived(
            new AppendEntriesRequest
            {
                LeaderId = leader.Id,
                LeaderCommitIndex = currentCommitIndex + 1,
                Term = leaderTerm,
                PrevLogIndex = currentIndex,
                PrevLogTerm = currentTerm,
                Entries = newEntries
            },
            leader.Address);

        var idleRun = await _subject.OnLoopRunProxy(); // process the message arrived

        // assert
        idleRun.Should().BeFalse(); // loop was not idle

        _subject.Status.Should().Be(RaftNodeStatus.Follower); // still follower
        _subject.CurrentTerm.Should().Be(leaderTerm); // still same term

        // checks if self logs are in the same term as the prev ones in the append request
        if (currentCommitIndex > 0)
        {
            _logRepositoryMock.Verify(x => x.GetTermAtIndex(currentCommitIndex), Times.Once());
        }
        // appends the new entries
        _logRepositoryMock.Verify(x => x.Append(It.Is<IEnumerable<LogEntry>>(a => a.Contains(newEntry))), Times.Once());
        // commits the new log as it was commited by the leader
        _logRepositoryMock.Verify(x => x.Commit(currentCommitIndex + 1), Times.Once());
        _logRepositoryMock.Verify(x => x.GetLogsAtIndex(currentCommitIndex + 1, 1), Times.Once());
        _logRepositoryMock.VerifyGet(x => x.LastIndex, Times.AtLeast(1));
        _logRepositoryMock.VerifyGet(x => x.CommitedIndex, Times.AtLeast(1));
        _logRepositoryMock.VerifyNoOtherCalls();

        _messageSenderMock.Verify(x => x.SendMessage(It.Is<AppendEntriesResponse>(r => r.Success), leader.Address), Times.Once());
        _messageSenderMock.VerifyNoOtherCalls();
    }

    private async Task FromFollowerToCandidate()
    {
        await _subject.OnLoopRunProxy();
        // becomes follower

        _now = _now.Add(_options.LeaderTimeout).AddSeconds(1); // advance time
        await _subject.OnLoopRunProxy();
        // becomes a candidate
    }
}