namespace SlimCluster.Consensus.Raft.Logs;

/// <summary>
/// A log repository that recovers its log and applied index on construction and makes appends
/// durable before returning. Implementations must preserve configuration entries and their suffix
/// replacement semantics. Voter reconfiguration currently requires retaining the full log.
/// </summary>
public interface IDurableRaftLogRepository : ILogRepository
{
}
