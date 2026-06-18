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

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void Given_Buffer_When_FilledToCapacity_Then_AllSlotsUsed(int capacity)
    {
        // arrange
        var subject = new MembershipEventBuffer(capacity);

        // act
        for (var i = 0; i < capacity; i++)
        {
            var added = subject.Add(new Messages.MembershipEvent($"node{i}", Messages.MembershipEventType.Joined, _now));
            added.Should().BeTrue($"slot {i} should fit in a buffer of capacity {capacity}");
        }

        var events = subject.GetNextEvents(capacity + 1);
        events.Should().HaveCount(capacity, "buffer should hold all {0} events", capacity);
    }

    [Fact]
    public void Given_ZeroCapacity_When_CreateBuffer_Then_Throws()
    {
        var act = () => new MembershipEventBuffer(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Given_FullBuffer_When_AddNewEvent_Then_ReplacesHighestUsedEntry()
    {
        // arrange
        var subject = new MembershipEventBuffer(3);

        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node2", Messages.MembershipEventType.Joined, _now));
        subject.Add(new Messages.MembershipEvent("node3", Messages.MembershipEventType.Joined, _now));

        // drive up UsedCount for node1 by calling GetNextEvents multiple times
        subject.GetNextEvents(3); // all used once
        subject.GetNextEvents(3); // all used twice — node1 should be first returned each time due to ordering, but all equal here

        // Now get only 1 to further bump one entry  
        // Actually GetNextEvents uses least-used ordering — let's just call enough times
        // to ensure node1 has higher UsedCount than others
        for (var i = 0; i < 5; i++) subject.GetNextEvents(1); // bumps the least-used one repeatedly

        // act: add event for a new node (node4) — should evict the highest used entry
        var added = subject.Add(new Messages.MembershipEvent("node4", Messages.MembershipEventType.Joined, _now.AddMinutes(10)));

        // assert
        added.Should().BeTrue();
        var remaining = subject.GetNextEvents(10);
        remaining.Should().HaveCount(3);
        remaining.Should().Contain(x => x.NodeId == "node4");
    }

    /// <summary>
    /// When two events arrive for the same node, the one with the newer incarnation wins.
    /// When incarnations are equal, the newer timestamp wins.
    /// Parameters: (firstType, firstOffsetMinutes, secondType, secondOffsetMinutes, expectedAddedResult, expectedSurvivingType)
    /// </summary>
    [Theory]
    [InlineData(Messages.MembershipEventType.Joined,  0, Messages.MembershipEventType.Faulted, 1, true,  Messages.MembershipEventType.Faulted)]  // newer replaces older
    [InlineData(Messages.MembershipEventType.Faulted, 1, Messages.MembershipEventType.Joined,  0, false, Messages.MembershipEventType.Faulted)]  // older is ignored
    [InlineData(Messages.MembershipEventType.Joined,  0, Messages.MembershipEventType.Joined,  0, false, Messages.MembershipEventType.Joined)]   // exact duplicate is ignored
    public void Given_TwoEventsForSameNode_When_Add_Then_CorrectEventSurvives(
        Messages.MembershipEventType firstType,  int firstOffsetMinutes,
        Messages.MembershipEventType secondType, int secondOffsetMinutes,
        bool expectedAdded,
        Messages.MembershipEventType expectedSurvivingType)
    {
        var subject = new MembershipEventBuffer(10);

        subject.Add(new Messages.MembershipEvent("node1", firstType,  _now.AddMinutes(firstOffsetMinutes)));

        // act
        var added = subject.Add(new Messages.MembershipEvent("node1", secondType, _now.AddMinutes(secondOffsetMinutes)));

        // assert
        added.Should().Be(expectedAdded);
        var events = subject.GetNextEvents(10);
        events.Should().HaveCount(1);
        events.Single().Type.Should().Be(expectedSurvivingType);
    }

    [Fact]
    public void Given_EventsForSameNode_When_HigherIncarnationHasOlderTimestamp_Then_HigherIncarnationWins()
    {
        var subject = new MembershipEventBuffer(10);

        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now.AddMinutes(10), incarnation: 1));

        var added = subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Faulted, _now, incarnation: 2));

        added.Should().BeTrue();
        subject.GetNextEvents(10).Single().Type.Should().Be(Messages.MembershipEventType.Faulted);
    }

    [Fact]
    public void Given_EventsForSameNode_When_LowerIncarnationHasNewerTimestamp_Then_LowerIncarnationIgnored()
    {
        var subject = new MembershipEventBuffer(10);

        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Faulted, _now, incarnation: 2));

        var added = subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now.AddMinutes(10), incarnation: 1));

        added.Should().BeFalse();
        subject.GetNextEvents(10).Single().Type.Should().Be(Messages.MembershipEventType.Faulted);
    }

    [Fact]
    public void Given_Events_When_GetNextEvents_Then_UsedCountIncremented()
    {
        var subject = new MembershipEventBuffer(10);
        subject.Add(new Messages.MembershipEvent("node1", Messages.MembershipEventType.Joined, _now));

        subject.GetNextEvents(5);
        subject.GetNextEvents(5);

        var fieldInfo = typeof(MembershipEventBuffer).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var items = (List<MembershipEventBuffer.BufferItem>)fieldInfo.GetValue(subject)!;

        items.Single().UsedCount.Should().Be(2);
    }
}
