namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;

public class InMemoryLogRepositoryTests
{
    private readonly InMemoryLogRepository subject;

    public InMemoryLogRepositoryTests()
    {
        subject = new InMemoryLogRepository();
    }

    [Fact]
    public async Task When_Append_Given_EmptyLogs_Then_IndexIs1_And_LastIndexCorrect()
    {
        // arrange

        var command = new object();
        var term = 1;

        // act
        var index = await subject.Append(term, command);

        // assert
        index.Should().Be(1);
        subject.GetTermAtIndex(index).Should().Be(term);
        subject.LastIndex.Index.Should().Be(index);
        subject.LastIndex.Term.Should().Be(term);
    }

    [Fact]
    public void When_New_Given_EmptyLogs_Then_LastIndexIsZeroAndZero()
    {
        // arrange

        // act
        var lastIndex = subject.LastIndex;

        // assert
        lastIndex.Index.Should().Be(0);
        lastIndex.Term.Should().Be(0);
    }

    [Fact]
    public async Task When_GetLogsAtIndex_Given_LogsExist_Then_ReturnsLogRange()
    {
        // arrange
        var command = new object();
        var command2 = new object();
        var command3 = new object();
        var term = 1;

        await subject.Append(term, command);
        await subject.Append(term, command2);
        await subject.Append(term, command3);

        // act
        var logs = await subject.GetLogsAtIndex(2, 2);

        // assert
        logs.Should().HaveCount(2);
        logs[0].Should().BeSameAs(command2);
        logs[1].Should().BeSameAs(command3);
    }
}