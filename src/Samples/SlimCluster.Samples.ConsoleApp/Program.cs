namespace SlimCluster.Samples.ConsoleApp;

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimCluster.Membership;
using SlimCluster.Membership.Swim;
using SlimCluster.Serialization.Json;
using SlimCluster.Strategy.Raft;
using System.Text;

public class CommandLineOptions
{
    [Option(shortName: 'p', longName: "port", Required = false, HelpText = "UdpPort for this node", Default = 60001)]
    public int UdpPort { get; set; }

    [Option(shortName: 'm', longName: "multicast-group", Required = false, HelpText = "UDP multicast group", Default = "239.1.1.1")]
    public string UdpMulticastGroupAddress { get; set; } = string.Empty;
}

public class Program
{
    public static Task Main(string[] args)
        => Parser.Default
            .ParseArguments<CommandLineOptions>(args)
            .MapResult(opts => RunHost(opts, args), errs => Task.FromResult(-1));

    private static Task RunHost(CommandLineOptions options, string[] args) => Host
        .CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton(options);
            services.AddHostedService<MainApp>();

            // Setup Swim Cluster Membership
            services.AddClusterMembership(opts =>
            {
                opts.Port = options.UdpPort;
                opts.MulticastGroupAddress = options.UdpMulticastGroupAddress;
                opts.ClusterId = "MyMicroserviceCluster";
                opts.MembershipEventPiggybackCount = 2;
            },
            serializerFactory: (svp) => new JsonSerializer(Encoding.ASCII));

            //// Membership config
            //services.AddSingleton<IClusterMembership>(svp => new StaticClusterMemberlist(clusterId, new INode[] { }));

            //// Raft consensus config
            //services.AddSingleton<RaftNode>();
            //services.AddSingleton<RaftCluster>(svp => new RaftCluster(clusterId));
            //services.AddSingleton<ICluster, RaftCluster>();

            //// App specific customization
            //services.AddTransient<IRaftTransport, AppRaftTransport>();
            //services.AddTransient<IStateMachine, AppStateMachine>();
        })
        .Build()
        .RunAsync();
}

public record MainApp(ILogger<MainApp> Logger, IClusterMembership ClusterMembership) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        void PrintActiveMembers() => Logger.LogInformation("This node is aware of {NodeList}", string.Join(", ", ClusterMembership.Members.Select(x => x.Node.ToString())));

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

        Logger.LogInformation("Node is starting...");
        await ClusterMembership.Start();

        Logger.LogInformation("Node is running");

        var taskCompletionSource = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (target, e) => taskCompletionSource.TrySetResult(null);

        await taskCompletionSource.Task;

        Logger.LogInformation("Node is stopping...");
        await ClusterMembership.Stop();
        Logger.LogInformation("Node is stopped");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// App specific way of implementing RPC calls for Raft algorithm.
/// </summary>
public class AppRaftTransport : IRaftTransport
{
    public Task<AppendEntriesResponse> AppendEntries(AppendEntriesRequest request, IAddress node)
        => throw new NotImplementedException();

    public Task<InstallSnapshotResponse> InstalSnapshot(InstallSnapshotRequest request, IAddress node)
        => throw new NotImplementedException();

    public Task<RequestVoteResponse> RequestVote(RequestVoteRequest request, IAddress node)
        => throw new NotImplementedException();
}

/// <summary>
/// App specific state representation (state machine)
/// </summary>
public class AppStateMachine : IStateMachine
{
    public Task Apply(IEnumerable<object> commands)
        => throw new NotImplementedException();
}
