namespace SlimCluster.Samples.Service.Controllers;

using Microsoft.AspNetCore.Mvc;

using SlimCluster.Consensus.Raft;
using SlimCluster.Samples.ConsoleApp.State.Logs;
using SlimCluster.Samples.ConsoleApp.State.StateMachine;

[ApiController]
[Route("[controller]")]
public class CounterController : ControllerBase
{
    private readonly ICounterState _counterState;
    private readonly IRaftClientRequestHandler _clientRequestHandler;

    public CounterController(ICounterState counterState, IRaftClientRequestHandler clientRequestHandler)
    {
        _counterState = counterState;
        _clientRequestHandler = clientRequestHandler;
    }

    [HttpGet()]
    public int Get() => _counterState.Counter;

    [HttpPost("[action]")]
    public async Task<int?> Increment(CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(new IncrementCounterCommand(), cancellationToken);
        return (int?)result;
    }

    [HttpPost("[action]")]
    public async Task<int?> Decrement(CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(new DecrementCounterCommand(), cancellationToken);
        return (int?)result;
    }

    [HttpPost("[action]")]
    public async Task<int?> Reset(CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(new ResetCounterCommand(), cancellationToken);
        return (int?)result;
    }
}