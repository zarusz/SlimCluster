namespace SlimCluster.Samples.ConsoleApp
{
    using CommandLine;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using SlimCluster.Membership;
    using SlimCluster.Membership.Swim;
    using SlimCluster.Strategy.Raft;
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

            clusterMembership.MemberJoined += (target, e) =>
            {
                Console.WriteLine("The node {0} joined", e.Node.Id);
                Console.WriteLine("This node is aware of {0}", string.Join(", ", clusterMembership.Members.Select(x => x.Node.ToString())));
            };

            clusterMembership.MemberLeft += (target, e) =>
            {
                Console.WriteLine("The node {0} left", e.Node.Id);
            };

            clusterMembership.MemberStatusChanged += (target, e) =>
            {
                if (e.Node.Status == SwimMemberStatus.Suspicious)
                {
                    Console.WriteLine("The node {0} is suspicious. All active members are: {1}", e.Node.Id, string.Join(", ", clusterMembership.Members.Where(x => x.Node.Status == SwimMemberStatus.Active)));
                }
            };

            Console.WriteLine("Node is starting...");
            await clusterMembership.Start();

            Console.WriteLine("Node is running");

            var taskCompletionSource = new TaskCompletionSource<object?>();
            Console.CancelKeyPress += (target, e) => taskCompletionSource.TrySetResult(null);

            await taskCompletionSource.Task;

            Console.WriteLine("Node is stopping...");
            await clusterMembership.Stop();
            Console.WriteLine("Node is stopped");
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
            });

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
