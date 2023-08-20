namespace SlimCluster.AspNetCore;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using SlimCluster.Consensus.Raft;

public class ClusterLeaderRequestDelegatingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICluster _cluster;
    private readonly RequestDelegatingClient _requestDelegatingClient;
    private readonly ClusterAspNetOptions _options;

    public ClusterLeaderRequestDelegatingMiddleware(RequestDelegate next, ICluster cluster, RequestDelegatingClient requestDelegatingClient, IOptions<ClusterAspNetOptions> options)
    {
        _next = next;
        _cluster = cluster;
        _requestDelegatingClient = requestDelegatingClient;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if request should be routed to leader
        if (_options.DelegateRequestToLeader != null && _options.DelegateRequestToLeader(context.Request))
        {
            var leaderAddress = _cluster.LeaderNode?.Address;
            if (leaderAddress == null)
            {
                throw new ClusterException("Leader not known at this time, retry request later on when leader is established");
            }

            if (!_cluster.SelfNode.Equals(_cluster.LeaderNode))
            {
                // This is a follower, so need to delegate the call to the leader
                await _requestDelegatingClient.Delegate(context.Request, context.Response, leaderAddress, localPort: context.Connection.LocalPort);
                return;
            }
        }
        // Not subject for routing, or this is the leader node (safe to pass the request processing by self)
        await _next(context);
    }
}
