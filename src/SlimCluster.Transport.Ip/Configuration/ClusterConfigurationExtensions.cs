namespace SlimCluster.Transport.Ip;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SlimCluster.Host;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddIpTransport(this ClusterConfiguration cfg, Action<IpTransportOptions> options)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.Configure(options);

            // Single IP/UDP endpoint
            services.TryAddSingleton<IPMessageEndpoint>();
            services.TryAddTransient(svp => new Lazy<IPMessageEndpoint>(() => svp.GetRequiredService<IPMessageEndpoint>()));
            services.TryAddSingleton<IMessageSender, IPMessageSender>();

            services.TryAddEnumerableEx(ServiceDescriptor.Transient<IClusterControlComponent>(svp => svp.GetRequiredService<IPMessageEndpoint>()));
        });
        return cfg;
    }
}
