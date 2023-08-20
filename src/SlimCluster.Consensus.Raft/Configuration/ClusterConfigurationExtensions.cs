namespace SlimCluster.Consensus.Raft;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SlimCluster.Host;
using SlimCluster.Persistence;
using SlimCluster.Serialization;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddRaftConsensus(this ClusterConfiguration cfg, Action<RaftConsensusOptions> options)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.Configure(options);

            services.TryAddSingleton<RaftNode>();
            services.TryAddEnumerableEx(ServiceDescriptor.Transient<IClusterControlComponent>(svp => svp.GetRequiredService<RaftNode>()));
            services.TryAddSingleton<ICluster, RaftCluster>();

            services.AddTransient<IMessageArrivedHandler>(svp => svp.GetRequiredService<RaftNode>());
            services.AddSingleton<ISerializationTypeAliasProvider, RaftSerializationTypeAliasProvider>();

            services.AddTransient<IDurableComponent>(svp => svp.GetRequiredService<RaftNode>());

            services.AddTransient<IRaftClientRequestHandler, RaftClientRequestHandler>();
        });
        return cfg;
    }
}
