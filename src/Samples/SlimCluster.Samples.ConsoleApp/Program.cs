namespace SlimCluster.Samples.ConsoleApp
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using SlimCluster.Membership;
    using SlimCluster.Strategy.Raft;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    class Program
    {
        static async Task Main(string[] args)
        {
            // Load configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var services = new ServiceCollection();

            new Startup(configuration).Configure(services);

            await using var serviceProvider = services.BuildServiceProvider();

            var raftCluster = serviceProvider.GetRequiredService<RaftCluster>();

            Console.WriteLine("Node is running");

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
    }

    public class Startup {

        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void Configure(ServiceCollection services)
        {
            var clusterId = "SomeCluster";

            // Membership config
            services.AddSingleton<IClusterMembership>(svp => new StaticClusterMemberlist(clusterId, new INode[] { }));

            // Raft consensus config
            services.AddSingleton<RaftNode>();
            services.AddSingleton<RaftCluster>(svp => new RaftCluster(clusterId));
            services.AddSingleton<ICluster, RaftCluster>();

            // App specific customization
            services.AddTransient<IRaftTransport, AppRaftTransport>();
            services.AddTransient<IStateMachine, AppStateMachine>();
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
