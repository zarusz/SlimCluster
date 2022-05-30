namespace SlimCluster.Membership.Swim.Tests;

public class SwimGossipTests
{
    private readonly DateTimeOffset _now;
    private readonly SwimClusterMembershipOptions _options;
    private readonly Mock<IMembershipEventListener> _membershipEventListenerMock;
    private readonly Mock<IMembershipEventBuffer> _membershipEventBufferMock;
    private readonly SwimGossip subject;

    public SwimGossipTests()
    {
        _now = DateTimeOffset.Parse("2022-06-13");
        _options = new SwimClusterMembershipOptions();
        _membershipEventListenerMock = new Mock<IMembershipEventListener>();
        _membershipEventBufferMock = new Mock<IMembershipEventBuffer>();

        subject = new SwimGossip(NullLogger<SwimGossip>.Instance, _options, _membershipEventListenerMock.Object, _membershipEventBufferMock.Object);
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

        var nodeMessage = new Messages.NodeMessage
        {
            Ping = new Messages.PingMessage(),
            Events = new[]
            {
                e1
            }
        };

        _membershipEventBufferMock.Setup(x => x.Add(e1)).Returns(addedToBuffer);

        // act
        await subject.OnMessageArrived(nodeMessage);

        // assert
        if (addedToBuffer)
        {
            if (eventType == Messages.MembershipEventType.Joined)
            {
                _membershipEventListenerMock.Verify(x => x.OnNodeJoined(e1.NodeId, It.Is<IPEndPoint>(ip => ip.ToString() == e1.NodeAddress)), Times.Once);
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
        var nodeMessage = new Messages.NodeMessage
        {
            Ping = new Messages.PingMessage(),                
        };

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(nodeMessage);

        // assert
        nodeMessage.Events.Should().BeSameAs(events);
    }

    [Fact]
    public void When_OnMessageSending_Given_AckMessage_And_MembershipEvents_Then_AddsToMessage()
    {
        // arrange
        var nodeMessage = new Messages.NodeMessage
        {
            Ack = new Messages.AckMessage(),
        };

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(nodeMessage);

        // assert
        nodeMessage.Events.Should().BeSameAs(events);
    }

    [Fact]
    public void When_OnMessageSending_Given_PingReqMessage_And_MembershipEvents_Then_AddsToMessage()
    {
        // arrange
        var nodeMessage = new Messages.NodeMessage
        {
            PingReq = new Messages.PingReqMessage(),
        };

        var events = new List<Messages.MembershipEvent>();
        _membershipEventBufferMock.Setup(x => x.GetNextEvents(It.IsAny<int>())).Returns(events);

        // act
        subject.OnMessageSending(nodeMessage);

        // assert
        nodeMessage.Events.Should().BeNull();
    }
}