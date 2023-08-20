namespace SlimCluster.Consensus.Raft;

using Microsoft.Extensions.DependencyInjection;

using SlimCluster.AspNetCore;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddAspNetCore(this ClusterConfiguration cfg, Action<ClusterAspNetOptions> options)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.AddHttpClient<RequestDelegatingClient>();

            services.Configure(options);
        });
        return cfg;
    }
}
