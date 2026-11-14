namespace SlimCluster.Consensus.Raft.Test;

using System.Collections.Concurrent;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Persistence.LocalFile;
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
                var entry = (await peer.Log.GetLogsAtIndex(2, 1)).Single();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Given_ReplacementLearner_When_ChangingVoters_Then_BothQuorumsAreRequired(bool failoverDuringJoint)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var members = Enumerable.Range(1, 4)
                .Select(i => (IMember)new Member(new Node($"node-{i}", $"192.168.1.{i}", 6200), DateTimeOffset.UnixEpoch))
                .ToArray();
            var initial = members.Take(3).Select(member => member.Node.Id).ToArray();
            var target = members.Skip(1).Select(member => member.Node.Id).ToArray();
            var options = Options.Create(new RaftConsensusOptions
            {
                VoterIds = initial,
                EnableVoterReconfiguration = true,
                LeaderPingInterval = TimeSpan.FromMilliseconds(100),
                RequestTimeout = TimeSpan.FromSeconds(15)
            });
            var network = new TestNetwork(members, options, directory.FullName);
            var oldLeader = network.Peers[0];
            var newcomer = network.Peers[3];
            network.Heal();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                foreach (var peer in network.Peers)
                {
                    await peer.Node.OnStartingProxy();
                }
                await network.Step();
                newcomer.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
                await network.Step();
                newcomer.Node.Status.Should().Be(RaftNodeStatus.Follower);
                newcomer.Node.CurrentTerm.Should().Be(0);

                oldLeader.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
                await network.StepUntil(() => oldLeader.Log.CommitedIndex == 1, deadline.Token);
                newcomer.Log.LastIndex.Index.Should().Be(0);

                // Only the old configuration can acknowledge the joint entry until the filter is removed.
                network.AllowAppend = (receiver, request) =>
                    request.Entries?.Any(entry => entry.VoterChange != null) != true || receiver == network.Peers[1];
                var change = oldLeader.Node.ChangeVoters(target, deadline.Token);
                await network.StepUntil(() => network.Peers[1].Log.LastIndex.Index == 2, deadline.Token);
                newcomer.Log.LastIndex.Index.Should().Be(1);
                oldLeader.Log.CommitedIndex.Should().Be(1);
                change.IsCompleted.Should().BeFalse();
                var joint = (await oldLeader.Log.GetLogsAtIndex(2, 1)).Single().VoterChange;
                joint.Should().NotBeNull();
                joint!.IsJoint.Should().BeTrue();

                if (failoverDuringJoint)
                {
                    network.AllowAppend = (_, request) =>
                        request.Entries?.Any(entry => entry.VoterChange is { IsJoint: false }) != true;
                    await network.StepUntil(() => oldLeader.Log.CommitedIndex == 2
                        && network.Peers.Skip(1).All(peer => peer.Log.LastIndex.Index == 2), deadline.Token);
                    await oldLeader.Node.OnNewerTermDiscovered(oldLeader.Node.CurrentTerm + 1, newcomer.Member.Node);
                    await Assert.ThrowsAsync<RaftClusterException>(() => change);
                    network.Isolate(oldLeader);
                }
                else
                {
                    network.AllowAppend = (receiver, request) => receiver != network.Peers[2]
                        || request.Entries?.Any(entry => entry.VoterChange is { IsJoint: false }) != true;
                    await network.StepUntil(() => change.IsCompleted, deadline.Token);
                    await change;
                    var final = (await oldLeader.Log.GetLogsAtIndex(3, 1)).Single().VoterChange;
                    final!.IsJoint.Should().BeFalse();
                    final.NewVoterIds.Should().Equal(target);
                }
                await network.StepUntil(() => oldLeader.Node.Status == RaftNodeStatus.Follower, deadline.Token);
                network.AllowAppend = null;

                // The promoted node can now lead. The removed node does not restart elections.
                newcomer.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
                if (!failoverDuringJoint)
                {
                    oldLeader.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
                }
                await network.StepUntil(() => newcomer.Node.Status == RaftNodeStatus.Leader, deadline.Token);
                oldLeader.Node.Status.Should().Be(RaftNodeStatus.Follower);
                var write = newcomer.Node.OnClientRequest("increment", deadline.Token);
                await network.StepUntil(() => write.IsCompleted, deadline.Token);
                (await write).Should().Be("increment");
                if (failoverDuringJoint)
                {
                    // The new leader resumes a committed joint entry without a client retry.
                    await network.StepUntil(() => newcomer.Log.CommitedIndex >= 5, deadline.Token);
                }

                var reopened = new FileRaftLogRepository(Path.Combine(directory.FullName, $"{newcomer.Member.Node.Id}.log"));
                reopened.CommitedIndex.Should().Be(newcomer.Log.CommitedIndex);
                var restoredVoters = new RaftVoterState(initial);
                await restoredVoters.ReadLog(reopened);
                restoredVoters.Current.NewIds.Should().Equal(target);
                restoredVoters.Current.Contains(oldLeader.Member.Node.Id).Should().BeFalse();
            }
            finally
            {
                foreach (var peer in network.Peers)
                {
                    await peer.Node.OnNewerTermDiscovered(peer.Node.CurrentTerm + 1, members[0].Node);
                }
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Given_AutomaticReplacement_When_VoterReturnsOrQuorumIsLost_Then_OnlySustainedLossWithQuorumIsReplaced()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var members = Enumerable.Range(1, 4)
                .Select(i => (IMember)new Member(new Node($"node-{i}", $"192.168.1.{i}", 6200), DateTimeOffset.UnixEpoch))
                .ToArray();
            var initial = members.Take(3).Select(member => member.Node.Id).ToArray();
            var target = new[] { "node-1", "node-3", "node-4" };
            var delay = TimeSpan.FromMilliseconds(350);
            var options = Options.Create(new RaftConsensusOptions
            {
                VoterIds = initial,
                EnableVoterReconfiguration = true,
                AutomaticVoterReplacementDelay = delay,
                LeaderPingInterval = TimeSpan.FromMilliseconds(100)
            });
            var network = new TestNetwork(members, options, directory.FullName);
            var leader = network.Peers[0];
            var lost = network.Peers[1];
            var survivor = network.Peers[2];
            var learner = network.Peers[3];
            network.Heal();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                foreach (var peer in network.Peers)
                {
                    await peer.Node.OnStartingProxy();
                }
                await network.Step();
                leader.Now += options.Value.LeaderTimeout + TimeSpan.FromSeconds(1);
                await network.StepUntil(() => leader.Log.CommitedIndex == 1, deadline.Token);

                network.Isolate(lost);
                await Observe(TimeSpan.FromMilliseconds(150));
                network.Heal();
                await Observe(delay);
                leader.Log.LastIndex.Index.Should().Be(1);
                network.LearnerProbes.Should().BeEmpty();

                network.Isolate(lost);
                network.Isolate(survivor);
                await Observe(delay + TimeSpan.FromMilliseconds(200));
                leader.Log.LastIndex.Index.Should().Be(1);
                network.LearnerProbes.Should().BeEmpty();

                network.Heal();
                network.Isolate(lost);
                var learnerAppend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                network.AllowAppend = (receiver, _) =>
                {
                    if (receiver != learner)
                    {
                        return true;
                    }
                    learnerAppend.TrySetResult();
                    return false;
                };
                await network.StepUntil(() => learnerAppend.Task.IsCompleted, deadline.Token, TimeSpan.FromMilliseconds(5));
                network.LearnerProbes.Should().Contain(learner.Member.Node.Id);
                leader.Log.LastIndex.Index.Should().Be(1);

                network.Heal();
                await Observe(delay);
                network.AllowAppend = null;
                await Observe(delay);
                leader.Log.LastIndex.Index.Should().Be(1);

                network.Isolate(lost);
                network.AllowAppend = (receiver, request) => receiver != survivor
                    || request.Entries?.Any(entry => entry.VoterChange is { IsJoint: true }) != true;
                await network.StepUntil(() => learner.Log.LastIndex.Index == 2, deadline.Token, TimeSpan.FromMilliseconds(5));
                leader.Log.CommitedIndex.Should().Be(1);
                network.AllowAppend = null;
                await network.StepUntil(() => leader.Log.CommitedIndex == 3, deadline.Token, TimeSpan.FromMilliseconds(5));
                var entries = await leader.Log.GetLogsAtIndex(2, 2);
                entries[0].VoterChange!.IsJoint.Should().BeTrue();
                entries[0].VoterChange!.OldVoterIds.Should().Equal(initial);
                entries[1].VoterChange!.IsJoint.Should().BeFalse();
                entries[1].VoterChange!.NewVoterIds.Should().Equal(target);
                learner.Log.LastIndex.Index.Should().Be(3);
            }
            finally
            {
                foreach (var peer in network.Peers)
                {
                    await peer.Node.OnNewerTermDiscovered(peer.Node.CurrentTerm + 1, members[0].Node);
                }
            }

            Task Observe(TimeSpan duration)
            {
                var until = leader.Now + duration;
                return network.StepUntil(() => leader.Now >= until, deadline.Token, TimeSpan.FromMilliseconds(5));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class TestNetwork
    {
        private readonly IMember[] _voters;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RaftResponse>> _requests = new();

        public Peer[] Peers { get; }
        public Func<Peer, AppendEntriesRequest, bool>? AllowAppend { get; set; }
        public ConcurrentQueue<string> LearnerProbes { get; } = new();

        public TestNetwork(IMember[] voters, IOptions<RaftConsensusOptions> options, string? directory = null)
        {
            _voters = voters;
            Peers = voters.Select((voter, index) =>
            {
                var partition = index < 2 ? voters.Take(2) : voters.Skip(2);
                var peerOptions = options;
                if (directory != null && !options.Value.VoterIds.Contains(voter.Node.Id))
                {
                    peerOptions = Options.Create(new RaftConsensusOptions
                    {
                        VoterIds = options.Value.VoterIds,
                        EnableVoterReconfiguration = true,
                        JoinAsLearner = true,
                        LeaderPingInterval = options.Value.LeaderPingInterval
                    });
                }
                return new Peer(voter, partition, peerOptions, this, directory);
            }).ToArray();
        }

        public async Task Step()
        {
            foreach (var peer in Peers)
            {
                await peer.Node.OnLoopRunProxy();
            }
        }

        public async Task StepUntil(Func<bool> condition, CancellationToken token, TimeSpan? step = null)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Step();
                foreach (var peer in Peers)
                {
                    peer.Now += step ?? TimeSpan.FromMilliseconds(1);
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

        public void Isolate(Peer isolated)
        {
            isolated.Discovered = Array.Empty<IMember>();
            foreach (var peer in Peers.Where(peer => peer != isolated))
            {
                peer.Discovered = peer.Discovered.Where(member => member != isolated.Member).ToArray();
            }
        }

        public Task Send(Peer sender, object message, IAddress address)
        {
            var receiver = Peers.Single(peer => peer.Member.Node.Address.Equals(address));
            if (!sender.Discovered.Contains(receiver.Member)
                || (message is AppendEntriesRequest append && AllowAppend?.Invoke(receiver, append) == false))
            {
                return Task.CompletedTask;
            }

            if (message is RaftResponse response && _requests.TryGetValue(response.RequestId, out var pending))
            {
                pending.TrySetResult(response);
                return Task.CompletedTask;
            }

            return receiver.Node.OnMessageArrived(message, sender.Member.Node.Address);
        }

        public async Task<TResponse> Request<TResponse>(Peer sender, RaftMessage request, IAddress address, TimeSpan? timeout)
            where TResponse : RaftResponse
        {
            if (request is LearnerProbeRequest)
            {
                LearnerProbes.Enqueue(Peers.Single(peer => peer.Member.Node.Address.Equals(address)).Member.Node.Id);
            }
            var pending = new TaskCompletionSource<RaftResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.TryAdd(request.RequestId, pending);
            try
            {
                await Send(sender, request, address);
                using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(1));
                return (TResponse)await pending.Task.WaitAsync(deadline.Token);
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
        public ILogRepository Log { get; }
        public RaftNodeProxy Node { get; }

        public Peer(IMember member, IEnumerable<IMember> partition, IOptions<RaftConsensusOptions> options, TestNetwork network, string? directory)
        {
            Member = member;
            Log = directory == null ? new InMemoryLogRepository() : new FileRaftLogRepository(Path.Combine(directory, $"{member.Node.Id}.log"));
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
                    network.Request<AppendEntriesResponse>(this, (RaftMessage)request, address, timeout));
            sender.Setup(x => x.SendRequest(It.IsAny<LearnerProbeRequest>(), It.IsAny<IAddress>(), It.IsAny<TimeSpan?>()))
                .Returns((IRequest<LearnerProbeResponse> request, IAddress address, TimeSpan? timeout) =>
                    network.Request<LearnerProbeResponse>(this, (RaftMessage)request, address, timeout));
            Node = new RaftNodeProxy(NullLoggerFactory.Instance, services.Object, membership.Object,
                clock.Object, Log, sender.Object, machine.Object, options);
            if (directory != null)
            {
                services.Setup(x => x.GetService(typeof(IClusterPersistenceService)))
                    .Returns(new LocalJsonFileClusterPersistenceService(new[] { Node }, Path.Combine(directory, $"{member.Node.Id}.json"), Newtonsoft.Json.Formatting.None));
            }
        }
    }
}
