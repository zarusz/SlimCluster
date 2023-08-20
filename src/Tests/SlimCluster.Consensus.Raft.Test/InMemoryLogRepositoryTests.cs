namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;

public class InMemoryLogRepositoryTests
{
    private readonly InMemoryLogRepository _subject;
    private readonly Fixture _fixture;

    public InMemoryLogRepositoryTests()
    {
        _subject = new InMemoryLogRepository();
        _fixture = new Fixture();
    }

    [Fact]
    public async Task When_Append_Given_EmptyLogs_Then_IndexIs1_And_LastIndexCorrect()
    {
        // arrange
        var command = _fixture.Create<byte[]>();
        var term = 1;

        // act
        var index = await _subject.Append(term, command);

        // assert
        index.Should().Be(1);
        _subject.GetTermAtIndex(index).Should().Be(term);
        _subject.LastIndex.Index.Should().Be(index);
        _subject.LastIndex.Term.Should().Be(term);
    }

    [Fact]
    public void When_New_Given_EmptyLogs_Then_LastIndexIsZeroAndZero()
    {
        // arrange

        // act
        var lastIndex = _subject.LastIndex;

        // assert
        lastIndex.Index.Should().Be(0);
        lastIndex.Term.Should().Be(0);
    }

    [Fact]
    public async Task When_GetLogsAtIndex_Given_LogsExist_Then_ReturnsLogRange()
    {
        // arrange
        var command = _fixture.Create<byte[]>();
        var command2 = _fixture.Create<byte[]>();
        var command3 = _fixture.Create<byte[]>();
        var term = 1;

        await _subject.Append(term, command);
        await _subject.Append(term, command2);
        await _subject.Append(term, command3);

        var indexStart = 2;

        // act
        var logs = await _subject.GetLogsAtIndex(indexStart, 2);

        // assert
        logs.Should().HaveCount(2);
        logs[0].Index.Should().Be(indexStart);
        logs[0].Term.Should().Be(term);
        logs[0].Entry.Should().BeSameAs(command2);
        logs[1].Index.Should().Be(indexStart + 1);
        logs[1].Term.Should().Be(term);
        logs[1].Entry.Should().BeSameAs(command3);
    }

    [Fact]
    public async Task When_Append_Given_OneEntry_Then_EntryAdded_And_LastIndex_Adjusted()
    {
        // arrange
        var command = _fixture.Create<byte[]>();
        var command2 = _fixture.Create<byte[]>();
        var term = 1;

        var logs = new LogEntry[]
        {
            new LogEntry(1, term, command),
            new LogEntry(2, term, command2)
        };

        // act
        await _subject.Append(logs);

        // assert
        var lastIndex = _subject.LastIndex;

        lastIndex.Index.Should().Be(2);
        lastIndex.Term.Should().Be(1);

        var logsResult = await _subject.GetLogsAtIndex(1, 2);
        logsResult.Should().HaveCount(2);
        logsResult[0].Should().BeSameAs(logs[0]);
        logsResult[1].Should().BeSameAs(logs[1]);
    }
}