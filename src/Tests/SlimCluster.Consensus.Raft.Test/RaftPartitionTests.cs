namespace SlimCluster.Consensus.Raft.Test;

using System.Collections.Concurrent;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Serialization;
using SlimCluster.Transport;

public class RaftPartitionTests
{
    [Fact]
    public async Task Given_SingleVoter_When_ElectionStarts_Then_ItElectsItselfAndCommits()
    {
        var member = new Member(new Node("solo", "192.168.1.1", 6200), DateTimeOffset.UnixEpoch);
        var options = Options.Create(new RaftConsensusOptions { VoterIds = new[] { member.Node.Id } });
        var network = new TestNetwork(new IMember[] { member }, options);
        var peer = network.Peers.Single();
        try
        {
            await network.Step();
            peer.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
            await network.Step();

            peer.Node.Status.Should().Be(RaftNodeStatus.Leader);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            (await peer.Node.OnClientRequest("increment", deadline.Token)).Should().Be("increment");
            peer.Log.CommitedIndex.Should().Be(2);
        }
        finally
        {
            await peer.Node.OnNewerTermDiscovered(peer.Node.CurrentTerm + 1, member.Node);
        }
    }

    [Fact]
    public async Task Given_FiveVoters_When_Partitioned_Then_OnlyMajorityCommits_And_LogsConvergeAfterHealing()
    {
        var voters = Enumerable.Range(1, 5)
            .Select(i => (IMember)new Member(new Node($"node-{i}", $"192.168.1.{i}", 6200), DateTimeOffset.UnixEpoch))
            .ToArray();
        var options = Options.Create(new RaftConsensusOptions
        {
            VoterIds = voters.Select(voter => voter.Node.Id).ToArray(),
            LeaderPingInterval = TimeSpan.FromMilliseconds(100)
        });
        var network = new TestNetwork(voters, options);
        var minority = network.Peers[0];
        var majority = network.Peers[2];

        try
        {
            await network.Step();
            minority.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
            await network.Step();
            await network.Step();
            minority.Node.Status.Should().Be(RaftNodeStatus.Candidate);
            await Assert.ThrowsAsync<ClusterException>(() => minority.Node.OnClientRequest("increment", default));
            minority.Log.CommitedIndex.Should().Be(0);

            majority.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
            await network.Step();
            await network.Step();
            await network.Step();
            majority.Node.Status.Should().Be(RaftNodeStatus.Leader);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var write = majority.Node.OnClientRequest("increment", deadline.Token);
            await network.StepUntil(() => write.IsCompleted, deadline.Token);
            (await write).Should().Be("increment");
            majority.Log.CommitedIndex.Should().Be(2);
            network.Peers.Take(2).Should().OnlyContain(peer => peer.Log.CommitedIndex == 0);

            network.Heal();
            await network.StepUntil(() => network.Peers.All(peer => peer.Log.CommitedIndex == 2), deadline.Token);
            foreach (var peer in network.Peers)
            {
                var entry = await peer.Log.GetByIndex(2);
                entry.Term.Should().Be(majority.Node.CurrentTerm);
                entry.Entry.Should().Equal(1);
            }
        }
        finally
        {
            foreach (var peer in network.Peers)
            {
                // Stepping down joins the leader loop even though the node loop is driven manually.
                await peer.Node.OnNewerTermDiscovered(peer.Node.CurrentTerm + 1, voters[0].Node);
            }
        }
    }

    private sealed class TestNetwork
    {
        private readonly IMember[] _voters;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AppendEntriesResponse>> _requests = new();

        public Peer[] Peers { get; }

        public TestNetwork(IMember[] voters, IOptions<RaftConsensusOptions> options)
        {
            _voters = voters;
            Peers = voters.Select((voter, index) =>
            {
                var partition = index < 2 ? voters.Take(2) : voters.Skip(2);
                return new Peer(voter, partition, options, this);
            }).ToArray();
        }

        public async Task Step()
        {
            foreach (var peer in Peers)
            {
                await peer.Node.OnLoopRunProxy();
            }
        }

        public async Task StepUntil(Func<bool> condition, CancellationToken token)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Step();
                foreach (var peer in Peers)
                {
                    peer.Now += TimeSpan.FromMilliseconds(1);
                }
                await Task.Delay(1, token);
            }
        }

        public void Heal()
        {
            foreach (var peer in Peers)
            {
                peer.Discovered = _voters.Where(voter => voter != peer.Member).ToArray();
            }
        }

        public Task Send(Peer sender, object message, IAddress address)
        {
            var receiver = Peers.Single(peer => peer.Member.Node.Address.Equals(address));
            if (!sender.Discovered.Contains(receiver.Member))
            {
                return Task.CompletedTask;
            }

            if (message is AppendEntriesResponse response && _requests.TryGetValue(response.RequestId, out var pending))
            {
                pending.TrySetResult(response);
                return Task.CompletedTask;
            }

            return receiver.Node.OnMessageArrived(message, sender.Member.Node.Address);
        }

        public async Task<AppendEntriesResponse> Append(Peer sender, AppendEntriesRequest request, IAddress address, TimeSpan? timeout)
        {
            var pending = new TaskCompletionSource<AppendEntriesResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.TryAdd(request.RequestId, pending);
            try
            {
                await Send(sender, request, address);
                using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(1));
                return await pending.Task.WaitAsync(deadline.Token);
            }
            finally
            {
                _requests.TryRemove(request.RequestId, out _);
            }
        }
    }

    private sealed class Peer
    {
        public IMember Member { get; }
        public IReadOnlyCollection<IMember> Discovered { get; set; }
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public InMemoryLogRepository Log { get; } = new();
        public RaftNodeProxy Node { get; }

        public Peer(IMember member, IEnumerable<IMember> partition, IOptions<RaftConsensusOptions> options, TestNetwork network)
        {
            Member = member;
            Discovered = partition.Where(voter => voter != member).ToArray();
            var membership = new Mock<IClusterMembership>();
            membership.SetupGet(x => x.SelfMember).Returns(Member);
            membership.SetupGet(x => x.OtherMembers).Returns(() => Discovered);
            var clock = new Mock<ITime>();
            clock.SetupGet(x => x.Now).Returns(() => Now);
            var serializer = new SerializerMock();
            serializer.SetupSerDes("increment", new byte[] { 1 });
            var services = new Mock<IServiceProvider>();
            services.Setup(x => x.GetService(typeof(ISerializer))).Returns(serializer.Object);
            var machine = new Mock<IStateMachine>();
            machine.Setup(x => x.Apply("increment", It.IsAny<int>())).ReturnsAsync("increment");
            var sender = new Mock<IMessageSender>();
            sender.Setup(x => x.SendMessage(It.IsAny<object>(), It.IsAny<IAddress>()))
                .Returns((object message, IAddress address) => network.Send(this, message, address));
            sender.Setup(x => x.SendRequest(It.IsAny<AppendEntriesRequest>(), It.IsAny<IAddress>(), It.IsAny<TimeSpan?>()))
                .Returns((IRequest<AppendEntriesResponse> request, IAddress address, TimeSpan? timeout) =>
                    network.Append(this, (AppendEntriesRequest)request, address, timeout));
            Node = new RaftNodeProxy(NullLoggerFactory.Instance, services.Object, membership.Object,
                clock.Object, Log, sender.Object, machine.Object, options);
        }
    }
}
