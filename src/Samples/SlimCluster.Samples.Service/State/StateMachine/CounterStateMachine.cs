namespace SlimCluster.Samples.ConsoleApp.State.StateMachine;

using SlimCluster.Consensus.Raft;
using SlimCluster.Samples.ConsoleApp.State.Logs;

/// <summary>
/// Counter state machine that processes counter commands. Everything is stored in memory.
/// </summary>
public class CounterStateMachine : IStateMachine, ICounterState
{
    private int _intex = 0;
    private int _counter = 0;

    public int CurrentIndex => _intex;

    /// <summary>
    /// The counter value
    /// </summary>
    public int Counter => _counter;

    public Task<object?> Apply(object command, int index)
    {
        // Note: This is thread safe - there is ever going to be only one task at a time calling Apply

        if (_intex + 1 != index)
        {
            throw new InvalidOperationException($"The State Machine can only apply next command at index ${_intex + 1}");
        }

        int? result = command switch
        {
            IncrementCounterCommand => ++_counter,
            DecrementCounterCommand => --_counter,
            ResetCounterCommand => _counter = 0,
            _ => throw new NotImplementedException($"The command type ${command?.GetType().Name} is not supported")
        };

        _intex = index;

        return Task.FromResult<object?>(result);
    }

    // For now we don't support snapshotting
    public Task Restore() => throw new NotImplementedException();

    // For now we don't support snapshotting
    public Task Snapshot() => throw new NotImplementedException();
}
