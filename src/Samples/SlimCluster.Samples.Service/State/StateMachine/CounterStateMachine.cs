namespace SlimCluster.Samples.ConsoleApp.State.StateMachine;

using System.Text.Json;

using SlimCluster.Consensus.Raft;
using SlimCluster.Samples.ConsoleApp.State.Logs;

/// <summary>
/// Counter state machine that processes counter commands. Everything is stored in memory.
/// </summary>
public class CounterStateMachine : IStateMachine, ICounterState
{
    private sealed record CounterSnapshot(int Index, int Counter);

    private int _index = 0;
    private int _counter = 0;

    public int CurrentIndex => _index;

    /// <summary>
    /// The counter value
    /// </summary>
    public int Counter => _counter;

    public Task<object?> Apply(object command, int index)
    {
        // Note: This is thread safe - there is ever going to be only one task at a time calling Apply

        if (_index + 1 != index)
        {
            throw new InvalidOperationException($"The State Machine can only apply next command at index ${_index + 1}");
        }

        int? result = command switch
        {
            IncrementCounterCommand => ++_counter,
            DecrementCounterCommand => --_counter,
            ResetCounterCommand => _counter = 0,
            _ => throw new NotSupportedException($"The command type ${command?.GetType().Name} is not supported")
        };

        _index = index;

        return Task.FromResult<object?>(result);
    }

    public Task Restore() => Task.CompletedTask;

    public Task<byte[]> Snapshot()
    {
        var snapshot = new CounterSnapshot(_index, _counter);
        return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(snapshot));
    }

    public Task InstallSnapshot(byte[] snapshot, int lastIncludedIndex, int lastIncludedTerm)
    {
        var state = JsonSerializer.Deserialize<CounterSnapshot>(snapshot)
            ?? throw new InvalidOperationException("The counter snapshot payload is empty or invalid.");

        if (state.Index != lastIncludedIndex)
        {
            throw new InvalidOperationException("The counter snapshot index does not match the Raft snapshot metadata.");
        }

        _index = state.Index;
        _counter = state.Counter;

        return Task.CompletedTask;
    }
}
