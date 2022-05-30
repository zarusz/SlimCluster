namespace SlimCluster.Membership.Swim.Tests;

using System.Reflection;

public class SwimMembershipEventBufferTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2022-06-05T00:00:00Z");

    [Fact]
    public void Given_SomeEvents_When_Add_Then_ReplacesOlderEventForTheSameNode()
    {
        // arrange
        var subject = new MembershipEventBuffer(20);

        // act
        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now)); // try to add a duplicate
        subject.Add(new Messages.MembershipEvent("node2", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node3", Messages.MembershipEventType.Joined, _now.AddMinutes(2)));
        subject.Add(new Messages.MembershipEvent("node2", Messages.MembershipEventType.Faulted, _now.AddMinutes(3)));

        // assert
        // get the private field
        var fieldInfo = typeof(MembershipEventBuffer).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("items is null");

        var items = (List<MembershipEventBuffer.BufferItem>)(fieldInfo.GetValue(subject)
            ?? throw new InvalidOperationException("count not get items value"));

        items.Should().HaveCount(3);
        
        items.Should().AllSatisfy(x => x.UsedCount.Should().Be(0));

        items.Should().ContainSingle(x => 
            x.MemberEvent.NodeId == "node1" 
            && x.MemberEvent.Type == Messages.MembershipEventType.Joined 
            && x.MemberEvent.Timestamp == _now);

        items.Should().ContainSingle(x => 
            x.MemberEvent.NodeId == "node2" 
            && x.MemberEvent.Type == Messages.MembershipEventType.Faulted 
            && x.MemberEvent.Timestamp == _now.AddMinutes(3));

        items.Should().ContainSingle(x => 
            x.MemberEvent.NodeId == "node3" 
            && x.MemberEvent.Type == Messages.MembershipEventType.Joined 
            && x.MemberEvent.Timestamp == _now.AddMinutes(2));
    }

    [Fact]
    public void Given_SomeEvents_When_GetNextEvents_Then_GetsLeastUsedAndYounger()
    {
        // arrange
        var subject = new MembershipEventBuffer(20);

        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node2", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node3", Messages.MembershipEventType.Joined, _now.AddMinutes(2)));
        subject.Add(new Messages.MembershipEvent("node2", Messages.MembershipEventType.Faulted, _now.AddMinutes(3)));

        // act
        var events = subject.GetNextEvents(2);

        // assert
        events.Should().HaveCount(2);
        events.Should().ContainSingle(x => 
            x.NodeId == "node2" 
            && x.Type == Messages.MembershipEventType.Faulted 
            && x.Timestamp == _now.AddMinutes(3));

        events.Should().ContainSingle(x => 
            x.NodeId == "node3" 
            && x.Type == Messages.MembershipEventType.Joined 
            && x.Timestamp == _now.AddMinutes(2));
    }
}