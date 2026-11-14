namespace SlimCluster.Consensus.Raft.Test;

using SlimCluster.Consensus.Raft.Logs;

public class RaftVoterStateTests
{
    [Fact]
    public async Task Given_UncommittedJointEntry_When_ReopenedAndOverwritten_Then_QuorumFollowsTheDurableLog()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "raft.log");
            var initial = new[] { "a", "b", "c" };
            var target = new[] { "b", "c", "d" };
            var log = new FileRaftLogRepository(path);
            await log.Append(new[]
            {
                new LogEntry(1, 1, Array.Empty<byte>()) { VoterChange = new RaftVoterChange(initial, target, true) }
            });

            log = new FileRaftLogRepository(path);
            var voters = new RaftVoterState(initial);
            await voters.ReadLog(log);

            log.CommitedIndex.Should().Be(0);
            voters.Current.HasQuorum(new HashSet<string> { "a", "b" }).Should().BeFalse();
            voters.Current.HasQuorum(new HashSet<string> { "b", "d" }).Should().BeFalse();
            voters.Current.HasQuorum(new HashSet<string> { "b", "c" }).Should().BeTrue();

            await log.Append(new[] { new LogEntry(1, 2, Array.Empty<byte>()) });
            await voters.ReadLog(new FileRaftLogRepository(path));
            voters.Current.IsJoint.Should().BeFalse();
            voters.Current.NewIds.Should().Equal(initial);
            voters.Current.HasQuorum(new HashSet<string> { "a", "b" }).Should().BeTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
