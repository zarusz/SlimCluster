namespace SlimCluster;

using Microsoft.Extensions.DependencyInjection;

public class ClusterConfiguration : ClusterOptions
{
    public IList<Action<IServiceCollection>> PostConfigurationActions { get; } = new List<Action<IServiceCollection>>();
}
