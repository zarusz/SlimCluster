namespace SlimCluster.Host.Common;

public abstract class TaskLoop
{
    private readonly ILogger _logger;
    private readonly TimeSpan _idleLoopDelay;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isStarted = false;
    private bool _isStarting = false;
    private bool _isStopping = false;

    public bool IsStarted => _isStarted;
    protected virtual TimeSpan StopTimeout => TimeSpan.FromSeconds(5);

    protected TaskLoop(ILogger logger, TimeSpan idleLoopDelay)
    {
        _logger = logger;
        _idleLoopDelay = idleLoopDelay;
    }

    protected TaskLoop(ILogger logger)
        : this(logger, TimeSpan.FromMilliseconds(50))
    {
    }
    public async Task Start()

    {
        if (!_isStarted && !_isStarting)
        {
            _isStarting = true;
            try
            {
                await OnStarting();

                _loopCts = new CancellationTokenSource();
                _loopTask = Task.Run(Loop);

                _isStarted = true;

                await OnStarted();
            }
            finally
            {
                _isStarting = false;
            }
        }
    }

    public async Task Stop()
    {
        if (_isStarted && !_isStopping)
        {
            _isStopping = true;
            try
            {
                if (_loopCts != null)
                {
                    _loopCts.Cancel();
                }

                await OnStopping();

                if (_loopTask != null)
                {
                    var completedTask = await Task.WhenAny(_loopTask, Task.Delay(StopTimeout));
                    if (completedTask == _loopTask)
                    {
                        await _loopTask;
                    }
                    else
                    {
                        _logger.LogError("Task loop did not stop within {StopTimeout}", StopTimeout);
                    }
                    _loopTask = null;
                }

                if (_loopCts != null)
                {
                    _loopCts.Dispose();
                    _loopCts = null;
                }

                _isStarted = false;

                await OnStopped();
            }
            finally
            {
                _isStopping = false;
            }
        }
    }

    private async Task Loop()
    {
        try
        {
            while (_loopCts != null && !_loopCts.IsCancellationRequested)
            {
                try
                {
                    var idleRun = await OnLoopRun(_loopCts.Token).ConfigureAwait(false);
                    if (idleRun)
                    {
                        await Task.Delay(_idleLoopDelay, _loopCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_loopCts?.IsCancellationRequested == true)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Loop error, will retry");
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Loop crashed");
        }
    }

    protected internal virtual Task OnStarted() => Task.CompletedTask;
    protected internal virtual Task OnStarting() => Task.CompletedTask;
    protected internal virtual Task OnStopped() => Task.CompletedTask;
    protected internal virtual Task OnStopping() => Task.CompletedTask;
    protected internal abstract Task<bool> OnLoopRun(CancellationToken token);
}
