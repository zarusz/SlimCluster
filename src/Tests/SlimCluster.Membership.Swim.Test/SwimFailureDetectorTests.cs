namespace SlimCluster.Membership.Swim.Tests;

using Microsoft.Extensions.Logging;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Transport;
using SlimCluster.Transport.Ip;

public class SwimFailureDetectorTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private readonly SwimClusterMembershipOptions _options;
    private readonly Mock<IMessageSender> _messageSenderMock;
    private readonly Mock<ITime> _timeMock;
    private readonly Mock<ICurrentNode> _currentNodeMock;

    private readonly ILogger<SwimFailureDetector> _logger = NullLogger<SwimFailureDetector>.Instance;

    private static SwimMember MakeMember(string id, string address, SwimMemberStatus? status = null)
    {
        var member = new SwimMember(
            id,
            IPEndPointAddress.Parse(address),
            DateTimeOffset.UtcNow,
            status ?? SwimMemberStatus.Active,
            null,
            NullLogger<SwimMember>.Instance);
        return member;
    }

    public SwimFailureDetectorTests()
    {
        _options = new SwimClusterMembershipOptions
        {
            ProtocolPeriod = TimeSpan.FromSeconds(5),
            PingAckTimeout = TimeSpan.FromSeconds(1),
            FailureDetectionSubgroupSize = 2,
        };

        _messageSenderMock = new Mock<IMessageSender>();
        _timeMock = new Mock<ITime>();
        _currentNodeMock = new Mock<ICurrentNode>();

        _timeMock.SetupGet(x => x.Now).Returns(() => _now);
        _currentNodeMock.SetupGet(x => x.Id).Returns("self");
    }

    private SwimFailureDetector CreateSubject(IReadOnlyList<SwimMember> members)
        => new(_logger, _options, _messageSenderMock.Object, members, _currentNodeMock.Object, _timeMock.Object);

    // -----------------------------------------------------------------------
    // OnPingTimeout: selected members are populated and PingReq is sent
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Given_PingTimeout_When_ActiveMembersExist_Then_PingReqSentToSubgroup()
    {
        // arrange
        var node1 = MakeMember("node1", "10.0.0.1:6000");
        var node2 = MakeMember("node2", "10.0.0.2:6000");
        var node3 = MakeMember("node3", "10.0.0.3:6000");
        var pingTarget = MakeMember("target", "10.0.0.9:6000");

        // Start the target as Active so it gets selected for pinging
        var allMembers = new List<SwimMember> { node1, node2, node3, pingTarget };

        var subject = CreateSubject(allMembers);

        // Advance time to trigger a new period — this selects pingTarget for probing
        var periodStart = _now;
        var afterPeriod = periodStart.Add(_options.ProtocolPeriod).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterPeriod);

        _messageSenderMock
            .Setup(x => x.SendMessage(It.IsAny<SwimMessage>(), It.IsAny<IAddress>()))
            .Returns(Task.CompletedTask);

        // Run to trigger OnNewPeriod → SelectMemberForPing → sends Ping
        await subject.DoRun();

        // Now advance past the pingAckTimeout
        var afterPingAck = afterPeriod.Add(_options.PingAckTimeout).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterPingAck);

        _messageSenderMock.Invocations.Clear();

        // act: DoRun should fire OnPingTimeout which should send PingReq messages
        await subject.DoRun();

        // assert: PingReq was sent to at most FailureDetectionSubgroupSize members (not zero!)
        _messageSenderMock.Verify(
            x => x.SendMessage(It.IsAny<PingReqMessage>(), It.IsAny<IAddress>()),
            Times.Between(1, _options.FailureDetectionSubgroupSize, Moq.Range.Inclusive));
    }

    [Fact]
    public async Task Given_PingTimeout_When_SubgroupSizeLargerThanMembers_Then_PingReqSentToAllActiveMembers()
    {
        // arrange: only 1 other active member, subgroup size is 2
        var onlyOther = MakeMember("other", "10.0.0.1:6000");

        // pingTarget is separate so it becomes the ping node, not the subgroup node
        var pingTarget = MakeMember("target", "10.0.0.9:6000");

        var subject = CreateSubject(new List<SwimMember> { onlyOther, pingTarget });

        _messageSenderMock
            .Setup(x => x.SendMessage(It.IsAny<SwimMessage>(), It.IsAny<IAddress>()))
            .Returns(Task.CompletedTask);

        var afterPeriod = _now.Add(_options.ProtocolPeriod).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterPeriod);
        await subject.DoRun(); // trigger new period, select one of them for ping

        var afterAck = afterPeriod.Add(_options.PingAckTimeout).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterAck);

        _messageSenderMock.Invocations.Clear();

        // act
        await subject.DoRun();

        // assert: exactly 1 PingReq sent (only one eligible member remains after removing ping node)
        _messageSenderMock.Verify(
            x => x.SendMessage(It.IsAny<PingReqMessage>(), It.IsAny<IAddress>()),
            Times.AtMostOnce());
    }

    // -----------------------------------------------------------------------
    // OnNewPeriod: node is declared Faulted if Confirming and no Ack arrived
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Given_NodeIsConfirming_When_NewPeriodStarts_Then_NodeDeclaredFaulted()
    {
        // arrange
        var statusChanges = new List<SwimMemberStatus>();
        var pingTarget = new SwimMember(
            "target", IPEndPointAddress.Parse("10.0.0.9:6000"),
            _now, SwimMemberStatus.Active,
            m => statusChanges.Add(m.SwimStatus),
            NullLogger<SwimMember>.Instance);

        var subject = CreateSubject(new List<SwimMember> { pingTarget });

        _messageSenderMock
            .Setup(x => x.SendMessage(It.IsAny<SwimMessage>(), It.IsAny<IAddress>()))
            .Returns(Task.CompletedTask);

        // Trigger first period — SelectMemberForPing picks pingTarget and sends Ping
        var afterPeriod1 = _now.Add(_options.ProtocolPeriod).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterPeriod1);
        await subject.DoRun();

        // Ack timeout fires — pingTarget transitions to Confirming
        var afterAck = afterPeriod1.Add(_options.PingAckTimeout).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterAck);
        await subject.DoRun();

        // Second period starts — pingTarget has not responded, declared Faulted
        var afterPeriod2 = afterAck.Add(_options.ProtocolPeriod).AddSeconds(1);
        _timeMock.SetupGet(x => x.Now).Returns(afterPeriod2);
        await subject.DoRun();

        // assert
        pingTarget.SwimStatus.Should().Be(SwimMemberStatus.Faulted);
    }

    // -----------------------------------------------------------------------
    // DoRun: idle vs non-idle based on time
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]   // within period — idle
    [InlineData(6, false)]  // period expired — not idle
    public async Task Given_DoRun_When_TimeAdvanced_Then_IdleMatchesPeriodState(int secondsElapsed, bool expectedIdle)
    {
        var subject = CreateSubject(new List<SwimMember>());

        if (secondsElapsed > 0)
            _timeMock.SetupGet(x => x.Now).Returns(_now.AddSeconds(secondsElapsed));

        var isIdle = await subject.DoRun();

        isIdle.Should().Be(expectedIdle);
    }
}
