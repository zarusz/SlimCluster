namespace SlimCluster.Consensus.Raft.Test;

using System.Text.Json;
using System.Text.Json.Nodes;

using Newtonsoft.Json;
using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Persistence.LocalFile;
using SlimCluster.Serialization;
using SlimCluster.Transport;

public class RaftBootstrapTests
{
    [Fact]
    public async Task Given_DiscoveredVoters_When_Bootstrapping_Then_AllPersistOneRosterBeforeElections()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var ids = new[] { "node-1", "node-2", "node-3" };
            var members = ids.Select((id, i) => (IMember)new Member(new Node(id, $"192.168.1.{i + 1}", 6200), DateTimeOffset.UnixEpoch)).ToArray();
            var store = new SharedFileRaftBootstrapStore(directory.FullName);
            var peers = members.Select(member => new Peer(directory.FullName, member, bootstrapDirectory: directory.FullName)).ToArray();
            foreach (var peer in peers)
            {
                await peer.Raft.OnStartingProxy();
            }

            // Only one node has discovered the full cohort.
            peers[0].Discovered = members[1..];
            await peers[0].Raft.OnLoopRunProxy();
            peers[0].ReadState()["bootstrapVoterIds"].Deserialize<string[]>().Should().Equal(ids);
            peers[0].Now += TimeSpan.FromHours(1);
            await peers[0].Raft.OnLoopRunProxy();
            peers[0].Raft.Status.Should().Be(RaftNodeStatus.Unknown);
            peers[0].Sender.VerifyNoOtherCalls();

            await peers[1].Raft.OnLoopRunProxy();
            Directory.CreateDirectory(peers[2].StatePath);
            await Assert.ThrowsAsync<ClusterException>(() => peers[2].Raft.OnLoopRunProxy());
            (await store.AreVotersReady("test-cluster", ids, default)).Should().BeFalse();
            await peers[0].Raft.OnLoopRunProxy();
            peers[0].Raft.Status.Should().Be(RaftNodeStatus.Unknown);

            Directory.Delete(peers[2].StatePath);
            await peers[2].Raft.OnLoopRunProxy();
            await peers[0].Raft.OnLoopRunProxy();
            await peers[1].Raft.OnLoopRunProxy();
            foreach (var peer in peers)
            {
                var state = peer.ReadState();
                state["bootstrapVoterIds"].Deserialize<string[]>().Should().Equal(ids);
                state["bootstrapComplete"]!.GetValue<bool>().Should().BeTrue();
            }

            // Restart from completed bootstrap without configuring a store again.
            var restarted = new Peer(directory.FullName, members[0]);
            await restarted.Raft.OnStartingProxy();
            restarted.Discovered = new[] { members[1] };
            await restarted.Raft.OnLoopRunProxy();
            restarted.Now += TimeSpan.FromHours(1);
            await restarted.Raft.OnLoopRunProxy();
            restarted.Raft.Status.Should().Be(RaftNodeStatus.Candidate);
            restarted.Sender.Verify(x => x.SendMessage(It.IsAny<RequestVoteRequest>(), members[1].Node.Address), Times.Once);

            var outsider = new Member(new Node("outsider", "192.168.1.4", 6200), DateTimeOffset.UnixEpoch);
            var newcomer = new Peer(directory.FullName, outsider, store, Path.Combine(directory.FullName, "unused"))
                { Discovered = members[1..] };
            await newcomer.Raft.OnStartingProxy();
            await newcomer.Raft.OnLoopRunProxy();
            newcomer.Raft.Status.Should().Be(RaftNodeStatus.Unknown);
            File.Exists(newcomer.StatePath).Should().BeFalse();

            File.Delete(peers[2].StatePath);
            var voterWithoutState = new Peer(directory.FullName, members[2], store);
            await voterWithoutState.Raft.OnStartingProxy();
            await Assert.ThrowsAsync<InvalidOperationException>(() => voterWithoutState.Raft.OnLoopRunProxy());
            voterWithoutState.Raft.Status.Should().Be(RaftNodeStatus.Unknown);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Given_NewCluster_When_NoSharedBootstrapStorageIsConfigured_Then_ReportsHowToConfigureIt()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var node = new Member(new Node("node-1", "192.168.1.1", 6200), DateTimeOffset.UnixEpoch);
            var peer = new Peer(directory.FullName, node);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => peer.Raft.OnStartingProxy());
            error.Message.Should().Contain("BootstrapDirectory").And.Contain("IRaftBootstrapStore");
        }
        finally
        {
            directory.Delete();
        }
    }

    [Fact]
    public async Task Given_CompetingBootstrapProposals_When_Published_Then_EveryCallerSeesTheSameRoster()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var first = new SharedFileRaftBootstrapStore(directory.FullName);
            var second = new SharedFileRaftBootstrapStore(directory.FullName);
            var left = new[] { "a", "b", "c" };
            var right = new[] { "d", "e", "f" };

            var results = await Task.WhenAll(
                Task.Run(() => first.GetOrPublishVoters("cluster", left, default)),
                Task.Run(() => second.GetOrPublishVoters("cluster", right, default)));

            results[0].Should().Equal(results[1]!);
            var published = await first.GetOrPublishVoters("cluster", null, default);
            published.Should().Equal(results[0]!);
            (published!.SequenceEqual(left) || published.SequenceEqual(right)).Should().BeTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class Peer
    {
        public IReadOnlyCollection<IMember> Discovered { get; set; } = Array.Empty<IMember>();
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public Mock<IMessageSender> Sender { get; } = new();
        public RaftNodeProxy Raft { get; }
        public string StatePath { get; }

        public Peer(string directory, IMember member, IRaftBootstrapStore? store = null, string? bootstrapDirectory = null)
        {
            StatePath = Path.Combine(directory, $"{member.Node.Id}.json");
            var membership = new Mock<IClusterMembership>();
            membership.SetupGet(x => x.ClusterId).Returns("test-cluster");
            membership.SetupGet(x => x.SelfMember).Returns(member);
            membership.SetupGet(x => x.OtherMembers).Returns(() => Discovered);
            var clock = new Mock<ITime>();
            clock.SetupGet(x => x.Now).Returns(() => Now);
            var services = new Mock<IServiceProvider>();
            services.Setup(x => x.GetService(typeof(ISerializer))).Returns(Mock.Of<ISerializer>());
            services.Setup(x => x.GetService(typeof(IRaftBootstrapStore))).Returns(store);
            Raft = new RaftNodeProxy(NullLoggerFactory.Instance, services.Object, membership.Object, clock.Object,
                new InMemoryLogRepository(), Sender.Object, Mock.Of<IStateMachine>(), Options.Create(new RaftConsensusOptions
                {
                    NodeCount = 3,
                    BootstrapDirectory = bootstrapDirectory
                }));
            services.Setup(x => x.GetService(typeof(IClusterPersistenceService)))
                .Returns(new LocalJsonFileClusterPersistenceService(new[] { Raft }, StatePath, Formatting.None));
        }

        public JsonNode ReadState() => JsonNode.Parse(File.ReadAllText(StatePath))![nameof(RaftNodeProxy)]!;
    }
}
