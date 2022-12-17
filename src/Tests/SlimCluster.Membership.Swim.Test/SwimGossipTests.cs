namespace SlimCluster.Membership.Swim.Tests;

using SlimCluster.Transport.Ip;

public class SwimGossipTests
{
    private readonly DateTimeOffset _now;
    private readonly SwimClusterMembershipOptions _options;
    private readonly Mock<IMembershipEventListener> _membershipEventListenerMock;
    private readonly Mock<IMembershipEventBuffer> _membershipEventBufferMock;
    private readonly SwimGossip subject;

    public SwimGossipTests(ITestOutputHelper testOutputHelper)
    {
        _now = DateTimeOffset.Parse("2022-06-13");
        _options = new SwimClusterMembershipOptions();
        _membershipEventListenerMock = new Mock<IMembershipEventListener>();
        _membershipEventBufferMock = new Mock<IMembershipEventBuffer>();

        subject = new SwimGossip(XUnitLogger.CreateLogger<SwimGossip>(testOutputHelper), _options, _membershipEventListenerMock.Object, _membershipEventBufferMock.Object);
    }

    [Theory]
    [InlineData(Messages.MembershipEventType.Joined, false)]
    [InlineData(Messages.MembershipEventType.Joined, true)]
    [InlineData(Messages.MembershipEventType.Faulted, true)]
    [InlineData(Messages.MembershipEventType.Faulted, false)]
    [InlineData(Messages.MembershipEventType.Left, true)]
    [InlineData(Messages.MembershipEventType.Left, false)]
    public async Task When_OnMessageArrived_Given_GossipEvents_Then_CallsCluster(Messages.MembershipEventType eventType, bool addedToBuffer)
    {
        // arrange
        var e1 = new Messages.MembershipEvent("node1", eventType, _now) { NodeAddress = "192.168.1.120:1234" };

        var msg = new Messages.PingMessage("node0")
        {
            Events = new[] { e1 }
        };

        _membershipEventBufferMock.Setup(x => x.Add(e1)).Returns(addedToBuffer);

        // act
        await subject.OnMessageArrived(msg, IPEndPointAddress.Parse(e1.NodeAddress));

        // assert
        if (addedToBuffer)
        {
            if (eventType == Messages.MembershipEventType.Joined)
            {
                _membershipEventListenerMock.Verify(x => x.OnNodeJoined(e1.NodeId, It.Is<IAddress>(ip => ip.ToString() == e1.NodeAddress)), Times.Once);
            }
            if (eventType == Messages.MembershipEventType.Faulted | eventType == Messages.MembershipEventType.Left)
            {
                _membershipEventListenerMock.Verify(x => x.OnNodeLeft(e1.NodeId), Times.Once);
            }
        }
        _membershipEventListenerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void When_OnMessageSending_Given_PingMessage_And_MembershipEvents_Then_AddsToMessage()
    {
        // arrange
        var msg = new Messages.PingMessage("node0");

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(msg);

        // assert
        msg.Events.Should().BeSameAs(events);
    }

    [Fact]
    public void When_OnMessageSending_Given_AckMessage_And_MembershipEvents_Then_AddsToMessage()
    {
        // arrange
        var msg = new Messages.AckMessage("node0");

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(msg);

        // assert
        msg.Events.Should().BeSameAs(events);
    }

    [Fact]
    public void When_OnMessageSending_Given_PingReqMessage_And_MembershipEvents_Then_AddsToMessage()
    {
        // arrange
        var msg = new Messages.PingReqMessage("node0");

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(msg);

        // assert
        msg.Events.Should().BeSameAs(events);
    }
}