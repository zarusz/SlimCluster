namespace SlimCluster.Membership.Swim;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SlimCluster.Host;
using SlimCluster.Membership.Swim.Messages;
using SlimCluster.Persistence;
using SlimCluster.Serialization;
using SlimCluster.Transport;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddSwimMembership(this ClusterConfiguration cfg, Action<SwimClusterMembershipOptions> options)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.Configure(options);

            services.AddSingleton<SwimClusterMembership>();

            services.TryAddTransient<IClusterMembership>(svp => svp.GetRequiredService<SwimClusterMembership>());
            services.TryAddTransient<ICurrentNode>(svp => (SwimMemberSelf)svp.GetRequiredService<SwimClusterMembership>().SelfMember);

            services.TryAddEnumerableEx(ServiceDescriptor.Transient<IClusterControlComponent>(svp => svp.GetRequiredService<SwimClusterMembership>()));

            services.AddTransient<IMessageSendingHandler>(svp => svp.GetRequiredService<SwimClusterMembership>());
            services.AddTransient<IMessageArrivedHandler>(svp => svp.GetRequiredService<SwimClusterMembership>());

            services.AddSingleton<ISerializationTypeAliasProvider, SwimSerializationTypeAliasProvider>();

            services.AddTransient<IDurableComponent>(svp => svp.GetRequiredService<SwimClusterMembership>());
        });
        return cfg;
    }
}
