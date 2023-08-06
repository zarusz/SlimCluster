namespace SlimCluster.Samples.ConsoleApp;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SlimCluster.Membership;
using SlimCluster.Membership.Swim;

public record MainApp(ILogger<MainApp> Logger, IClusterMembership ClusterMembership, ICluster Cluster) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        void PrintActiveMembers() => Logger.LogInformation("This node is aware of {NodeList}", string.Join(", ", ClusterMembership.Members.Select(x => x.Node.ToString())));

        // doc:fragment:ExampleMembershipChanges
        // Injected: IClusterMembership ClusterMembership
        ClusterMembership.MemberJoined += (target, e) =>
        {
            Logger.LogInformation("The member {NodeId} joined", e.Node.Id);
            PrintActiveMembers();
        };

        ClusterMembership.MemberLeft += (target, e) =>
        {
            Logger.LogInformation("The member {NodeId} left/faulted", e.Node.Id);
            PrintActiveMembers();
        };

        ClusterMembership.MemberStatusChanged += (target, e) =>
        {
            if (e.Node.Status == SwimMemberStatus.Suspicious)
            {
                Logger.LogInformation("The node {NodeId} is suspicious. All active members are: {NodeList}", e.Node.Id, string.Join(", ", ClusterMembership.Members.Where(x => x.Node.Status == SwimMemberStatus.Active)));
            }
        };
        // doc:fragment:ExampleMembershipChanges

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
