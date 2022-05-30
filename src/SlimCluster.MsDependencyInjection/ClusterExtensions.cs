namespace SlimCluster.MsDependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public static class ClusterExtensions
{
    public static IServiceCollection AddCluster(this IServiceCollection services)
    {
        return services;
    }
}
