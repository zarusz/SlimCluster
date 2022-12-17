namespace SlimCluster.Persistence.LocalFile;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Newtonsoft.Json;

using SlimCluster.Persistence;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddPersistenceUsingLocalFile(this ClusterConfiguration cfg, string filePath, Formatting formatting = Formatting.None)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.TryAddTransient(svp => new LocalJsonFileClusterPersistenceService(svp.GetServices<IDurableComponent>(), filePath, formatting));
            services.TryAddTransient<IClusterPersistenceService>(svp => svp.GetRequiredService<LocalJsonFileClusterPersistenceService>());
        });
        return cfg;
    }
}
