namespace SlimCluster.Consensus.Raft;

using Microsoft.AspNetCore.Http;

public class ClusterAspNetOptions
{
    /// <summary>
    /// Selects the request to be routed to the Leader node of the cluster. When not set then no requests are being routed to leader.
    /// </summary>
    public Func<HttpRequest, bool>? DelegateRequestToLeader { get; set; }

}
