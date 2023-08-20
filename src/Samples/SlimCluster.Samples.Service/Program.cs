using SlimCluster;
using SlimCluster.AspNetCore;
using SlimCluster.Consensus.Raft;
using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Membership.Swim;
using SlimCluster.Persistence.LocalFile;
using SlimCluster.Samples.ConsoleApp;
using SlimCluster.Samples.ConsoleApp.State.Logs;
using SlimCluster.Samples.ConsoleApp.State.StateMachine;
using SlimCluster.Serialization;
using SlimCluster.Serialization.Json;
using SlimCluster.Transport.Ip;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<MainApp>();

// doc:fragment:ExampleStartup
builder.Services.AddSlimCluster(cfg =>
{
    cfg.ClusterId = "MyCluster";
    // This will use the machine name, in Kubernetes this will be the pod name
    cfg.NodeId = Environment.MachineName;

    // Transport will be over UDP/IP
    cfg.AddIpTransport(opts =>
    {
        opts.Port = builder.Configuration.GetValue<int>("UdpPort");
        opts.MulticastGroupAddress = builder.Configuration.GetValue<string>("UdpMulticastGroupAddress")!;
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

        // Use custom values or remove and use defaults
        opts.LeaderTimeout = TimeSpan.FromSeconds(5);
        opts.LeaderPingInterval = TimeSpan.FromSeconds(2);
        opts.ElectionTimeoutMin = TimeSpan.FromSeconds(3);
        opts.ElectionTimeoutMax = TimeSpan.FromSeconds(6);
        // Can set a different log serializer, by default ISerializer is used (in our setup its JSON)
        // opts.LogSerializerType = typeof(JsonSerializer);
    });

    // Protocol messages (and logs/commands) will be serialized using JSON
    cfg.AddJsonSerialization();

    // Cluster state will saved into the local json file in between node restarts
    cfg.AddPersistenceUsingLocalFile("cluster-state.json");

    cfg.AddAspNetCore(opts =>
    {
        // Route all ASP.NET API requests for the Counter endpoint to the Leader node for handling
        opts.DelegateRequestToLeader = r => r.Path.HasValue && r.Path.Value.Contains("/Counter");
    });
});

// Raft app specific implementation
builder.Services.AddSingleton<ILogRepository, InMemoryLogRepository>(); // For now, store the logs in memory only
builder.Services.AddSingleton<IStateMachine, CounterStateMachine>(); // This is app specific machine that implements a distributed counter
builder.Services.AddSingleton<ISerializationTypeAliasProvider, CommandSerializationTypeAliasProvider>(); // App specific state/logs command types for the replicated state machine

// Requires packages: SlimCluster.Membership.Swim, SlimCluster.Consensus.Raft, SlimCluster.Serialization.Json, SlimCluster.Transport.Ip, SlimCluster.Persistence.LocalFile
// doc:fragment:ExampleStartup

builder.Services.AddTransient(svp => (ICounterState)svp.GetRequiredService<IStateMachine>());

//// Membership config
//services.AddSingleton<IClusterMembership>(svp => new StaticClusterMemberlist(clusterId, new INode[] { }));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

// Delegate selected ASP.NET API requests to the leader node for handling
app.UseClusterLeaderRequestDelegation();

app.MapControllers();

await app.RunAsync();
