namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using SlimCluster.Serialization;
    using System;

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddClusterMembership(
            this IServiceCollection services, 
            Action<SwimClusterMembershipOptions> options, 
            Func<IServiceProvider, ISerializer> serializerFactory)
        {
            services.Configure(options);

            services.AddSingleton<ITime, Time>();
            services.AddSingleton(serializerFactory);

            services.AddSingleton(svp => new SwimClusterMembership(svp.GetRequiredService<ILoggerFactory>(), svp.GetRequiredService<IOptions<SwimClusterMembershipOptions>>(), serializerFactory(svp), svp.GetRequiredService<ITime>()));
            services.AddSingleton<IClusterMembership>(svp => svp.GetRequiredService<SwimClusterMembership>());

            return services;
        }
    }
}
