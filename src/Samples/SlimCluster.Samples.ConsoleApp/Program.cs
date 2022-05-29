namespace SlimCluster.Samples.ConsoleApp
{
    using CommandLine;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using SlimCluster.Membership;
    using SlimCluster.Membership.Swim;
    using SlimCluster.Serialization.Json;
    using SlimCluster.Strategy.Raft;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class CommandLineOptions
    {
        [Option(shortName: 'p', longName: "port", Required = false, HelpText = "UdpPort for this node", Default = 60001)]
        public int UdpPort { get; set; }

        [Option(shortName: 'm', longName: "multicast-group", Required = false, HelpText = "UDP multicast group", Default = "239.1.1.1")]
        public string UdpMulticastGroupAddress { get; set; } = string.Empty;
    }

    class Program
    {
        static Task Main(string[] args) => Parser.Default
            .ParseArguments<CommandLineOptions>(args)
            .MapResult((CommandLineOptions opts) => DoMain(opts), errs => Task.FromResult(-1)); // Invalid arguments

        static async Task DoMain(CommandLineOptions options)
        {
            // Load configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var services = new ServiceCollection();

            new Startup(configuration).Configure(services, options);

            await using var serviceProvider = services.BuildServiceProvider();

            //var raftCluster = serviceProvider.GetRequiredService<RaftCluster>();
            var clusterMembership = serviceProvider.GetRequiredService<IClusterMembership>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            void PrintActiveMembers() => logger.LogInformation("This node is aware of {NodeList}", string.Join(", ", clusterMembership.Members.Select(x => x.Node.ToString())));

            clusterMembership.MemberJoined += (target, e) =>
            {
                logger.LogInformation("The member {NodeId} joined", e.Node.Id);
                PrintActiveMembers();
            };

            clusterMembership.MemberLeft += (target, e) =>
            {
                logger.LogInformation("The member {NodeId} left/faulted", e.Node.Id);
                PrintActiveMembers();
            };

            clusterMembership.MemberStatusChanged += (target, e) =>
            {
                if (e.Node.Status == SwimMemberStatus.Suspicious)
                {
                    logger.LogInformation("The node {NodeId} is suspicious. All active members are: {NodeList}", e.Node.Id, string.Join(", ", clusterMembership.Members.Where(x => x.Node.Status == SwimMemberStatus.Active)));
                }
            };

            logger.LogInformation("Node is starting...");
            await clusterMembership.Start();

            logger.LogInformation("Node is running");

            var taskCompletionSource = new TaskCompletionSource<object?>();
            Console.CancelKeyPress += (target, e) => taskCompletionSource.TrySetResult(null);

            await taskCompletionSource.Task;

            logger.LogInformation("Node is stopping...");
            await clusterMembership.Stop();
            logger.LogInformation("Node is stopped");
        }
    }

    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration) => Configuration = configuration;

        public void Configure(ServiceCollection services, CommandLineOptions options)
        {
            services.AddLogging(opts =>
            {
                opts.AddConfiguration(Configuration);
                opts.SetMinimumLevel(LogLevel.Debug);
                opts.AddConsole();
            });

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
        }
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
}
