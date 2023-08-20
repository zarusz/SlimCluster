namespace SlimCluster.Consensus.Raft;

using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Serialization;

public static class StateMachineExtensions
{
    public static async Task<object?> Apply(this IStateMachine stateMachine, ILogRepository logRepository, byte[] logEntry, int logIndex, ILogger logger, ISerializer logSerializer)
    {
        logger.LogTrace("Deserializing log at index {LogIndex}", logIndex);
        var command = logSerializer.Deserialize(logEntry);
        logger.LogDebug("Applying log at index {LogIndex}", logIndex);
        var commandResult = await stateMachine.Apply(command, logIndex).ConfigureAwait(false);
        logger.LogDebug("Commit log at index {LogIndex}", logIndex);
        await logRepository.Commit(logIndex).ConfigureAwait(false);

        return commandResult;
    }
}