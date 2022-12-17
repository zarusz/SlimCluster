namespace SlimCluster;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using SlimCluster.Host;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSlimCluster(this IServiceCollection services, Action<ClusterConfiguration>? configure = null)
    {
        // ClusterConfiguration
        if (!services.Any(x => x.ServiceType == typeof(ClusterConfiguration)))
        {
            // Register MessageBusBuilder for the root bus
            var cfg = new ClusterConfiguration();
            services.Add(ServiceDescriptor.Singleton(cfg));
        }

        services.AddSingleton<IConfigureOptions<ClusterOptions>>(svp => new ConfigureNamedOptions<ClusterOptions>(Options.DefaultName, opts =>
        {
            var cfg = svp.GetRequiredService<ClusterConfiguration>();

            opts.ClusterId = cfg.ClusterId;
            opts.NodeId = cfg.NodeId;
        }));

        services.TryAddSingleton<ITime, Time>();
        services.TryAddTransient<IClusterControl, ClusterControl>();
        services.AddHostedService<ClusterHostedService>();

        if (configure is not null)
        {
            // Execute the mbb setup for services registration
            var cfg = (ClusterConfiguration?)services
                .FirstOrDefault(x => x.ServiceType == typeof(ClusterConfiguration) && x.ImplementationInstance != null)
                ?.ImplementationInstance;

            if (cfg is not null)
            {
                configure(cfg);

                // Execute run config actions
                foreach (var action in cfg.PostConfigurationActions)
                {
                    action(services);
                }
            }
        }

        return services;
    }

    public static void TryAddEnumerableEx(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        var implementationFactory = descriptor.ImplementationFactory;
        if (implementationFactory != null)
        {
            if (!services.Any((d) => d.ServiceType == descriptor.ServiceType && ReferenceEquals(d.ImplementationFactory, implementationFactory)))
            {
                services.Add(descriptor);
            }
        }
        else
        {
            services.TryAddEnumerable(descriptor);
        }
    }
}
