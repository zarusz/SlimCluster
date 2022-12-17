namespace SlimCluster.Samples.ConsoleApp;

using CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SlimCluster.Consensus.Raft;
using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership;
using SlimCluster.Membership.Swim;
using SlimCluster.Persistence.LocalFile;
using SlimCluster.Serialization.Json;
using SlimCluster.Transport.Ip;

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

            // doc:fragment:ExampleStartup
            services.AddSlimCluster(cfg =>
            {
                cfg.ClusterId = "MyCluster";
                // This will use the machine name, in Kubernetes this will be the pod name
                cfg.NodeId = Environment.MachineName;

                // Transport will be over UDP/IP
                cfg.AddIpTransport(opts =>
                {
                    opts.Port = options.UdpPort;
                    opts.MulticastGroupAddress = options.UdpMulticastGroupAddress;
                });

                // Setup Swim Cluster Membership
                cfg.AddSwimMembership(opts =>
                {
                    opts.MembershipEventPiggybackCount = 2;
                });

                // Setup Raft Cluster Consensus
                cfg.AddRaftConsensus(opts =>
                {
                    opts.NodeCount = 3;
                });

                // Protocol messages will be serialized using JSON
                cfg.AddJsonSerialization();

                // Cluster state will saved into the local json file in between node restarts
                cfg.AddPersistenceUsingLocalFile("cluster-state.json");
            });

            // Raft app specific implementation
            services.AddSingleton<ILogRepository, InMemoryLogRepository>();
            services.AddTransient<IStateMachine, AppStateMachine>();
            
            // Requires packages: SlimCluster.Membership.Swim, SlimCluster.Consensus.Raft, SlimCluster.Serialization.Json, SlimCluster.Transport.Ip, SlimCluster.Persistence.LocalFile
            // doc:fragment:ExampleStartup

            //// Membership config
            //services.AddSingleton<IClusterMembership>(svp => new StaticClusterMemberlist(clusterId, new INode[] { }));
        })
        .Build()
        .RunAsync();
}

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

/// <summary>
/// App specific state representation (state machine)
/// </summary>
public class AppStateMachine : IStateMachine
{
    public int CurrentIndex => throw new NotImplementedException();
    public Task<object?> Apply(object command, int index) => throw new NotImplementedException();
    public Task Restore() => throw new NotImplementedException();
    public Task Snapshot() => throw new NotImplementedException();
}
