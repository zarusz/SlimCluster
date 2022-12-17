namespace SlimCluster.Host;

internal class ClusterControl : IClusterControl
{
    private readonly ILogger _logger;
    private readonly IEnumerable<IClusterControlComponent> _components;

    public ClusterControl(ILogger<ClusterControl> logger, IEnumerable<IClusterControlComponent> components)
    {
        _logger = logger;
        _components = components;
    }

    public async Task Start()
    {
        _logger.LogInformation("Node is starting...");
        foreach (var component in _components)
        {
            await component.Start().ConfigureAwait(false);
        }
        _logger.LogInformation("Node started");
    }

    public async Task Stop()
    {
        _logger.LogInformation("Node is stopping...");
        foreach (var component in _components.Reverse())
        {
            await component.Stop().ConfigureAwait(false);
        }
        _logger.LogInformation("Node stopped");
    }
}