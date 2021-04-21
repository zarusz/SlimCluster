namespace SlimCluster.Membership.Swim
{
    using Microsoft.Extensions.DependencyInjection;
    using SlimCluster.Membership.Swim.Serialization;
    using System;
    using System.Text;

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddClusterMembership(this IServiceCollection services, Action<SwimClusterMembershipOptions> options)
        {
            services.Configure(options);
            services.AddSingleton<SwimClusterMembership>();

            services.AddTransient<ISerializer>(svp => new JsonMessageSerializer(Encoding.ASCII));
            services.AddSingleton<IClusterMembership>(svp => svp.GetRequiredService<SwimClusterMembership>());

            return services;
        }
    }
}
