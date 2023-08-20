namespace SlimCluster.Consensus.Raft;
/// <summary>
/// State machine that holds state and is able to apply commands that mutate the machine state.
/// </summary>
public interface IStateMachine
{
    int CurrentIndex { get; }

    /// <summary>
    /// Applies command and provides result back to the client.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    Task<object?> Apply(object command, int index);

    /// <summary>
    /// Take a snapshot
    /// </summary>
    /// <returns></returns>
    Task Snapshot();

    /// <summary>
    /// Restores state machine from persisted snapshot
    /// </summary>
    /// <returns></returns>
    Task Restore();
}
