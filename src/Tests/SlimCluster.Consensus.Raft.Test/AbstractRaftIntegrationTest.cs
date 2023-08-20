namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Serialization;
using SlimCluster.Transport;

public abstract class AbstractRaftIntegrationTest
{
    protected readonly IFixture _fixture;
    protected readonly RaftConsensusOptions _options;
    protected readonly Mock<IServiceProvider> _serviceProviderMock;
    protected readonly Mock<IStateMachine> _stateMachineMock;
    protected readonly Mock<IClusterMembership> _clusterMembershipMock;
    protected readonly Mock<ITime> _timeMock;
    protected readonly Mock<InMemoryLogRepository> _logRepositoryMock;
    protected readonly Mock<IMessageSender> _messageSenderMock;
    protected readonly Mock<ISerializer> _serializerMock;

    protected DateTimeOffset _now = new(2023, 6, 25, 13, 0, 0, 0, TimeSpan.Zero);

    protected IMember _selfMember;
    protected List<IMember> _otherMembers;

    protected AbstractRaftIntegrationTest()
    {
        _fixture = new Fixture();
        _clusterMembershipMock = new Mock<IClusterMembership>();
        _timeMock = new Mock<ITime>();
        _logRepositoryMock = new Mock<InMemoryLogRepository> { CallBase = true };
        _messageSenderMock = new Mock<IMessageSender>();
        _serializerMock = new Mock<ISerializer>();

        _options = new RaftConsensusOptions
        {
            NodeCount = 3
        };

        static Node GetNode(int i) => new($"Node-{i:00}", $"192.168.1.{i}", 6200);

        _selfMember = new Member(GetNode(1), _now);
        _otherMembers = new List<IMember>
        {
            new Member(GetNode(2), _now),
            new Member(GetNode(3), _now),
        };

        _clusterMembershipMock.SetupGet(x => x.SelfMember).Returns(() => _selfMember);
        _clusterMembershipMock.SetupGet(x => x.OtherMembers).Returns(() => _otherMembers);

        _timeMock.SetupGet(x => x.Now).Returns(() => _now);

        _serviceProviderMock = new Mock<IServiceProvider>();
        _stateMachineMock = new Mock<IStateMachine>();

        _serviceProviderMock.Setup(x => x.GetService(typeof(ISerializer))).Returns(_serializerMock.Object);
    }
}
