namespace SlimCluster.Host;

using Microsoft.Extensions.Hosting;

internal class ClusterHostedService : IHostedService
{
    private readonly IClusterControl _clusterControl;

    public ClusterHostedService(IClusterControl clusterControl)
    {
        _clusterControl = clusterControl;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _clusterControl.Start();

    public Task StopAsync(CancellationToken cancellationToken) => _clusterControl.Stop();
}
