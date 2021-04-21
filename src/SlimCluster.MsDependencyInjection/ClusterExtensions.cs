using Microsoft.Extensions.DependencyInjection;
using System;

namespace SlimCluster.MsDependencyInjection
{
    public static class ClusterExtensions
    {
        public static IServiceCollection AddCluster(this IServiceCollection services)
        {

            return services;
        }
    }
}
