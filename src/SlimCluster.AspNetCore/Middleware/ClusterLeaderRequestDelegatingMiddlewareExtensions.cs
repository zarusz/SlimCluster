namespace SlimCluster.AspNetCore;

using Microsoft.AspNetCore.Builder;

public static class ClusterLeaderRequestDelegatingMiddlewareExtensions
{
    /// <summary>
    /// Use the Request Delegation to Leader node (if self is not a leader).
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static IApplicationBuilder UseClusterLeaderRequestDelegation(this IApplicationBuilder builder)
        => builder.UseMiddleware<ClusterLeaderRequestDelegatingMiddleware>();
}