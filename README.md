# SlimCluster

SlimCluster has the [Raft](https://raft.github.io/raft.pdf) distributed consensus algorithm implemented in .NET.
Additionally, it implements the [SWIM](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) cluster membership list (where nodes join and leave/die).

- Membership list is required to maintain what micro-service instances (nodes) constitute a cluster.
- Raft consensus helps propagate state across the micro-service instances and ensures there is a designated leader instance performing the coordination of work.

The library goal is to provide a common groundwork for coordination and consensus of your distributed micro-service instances.
With that, the developer can focus on the business problem at hand.
The library promises to have a friendly API and pluggable architecture.

The strategic aim for SlimCluster is to implement other algorithms to make distributed .NET micro-services easier and not require one to pull in a load of other 3rd party libraries or products.

[![Gitter](https://badges.gitter.im/SlimCluster/community.svg)](https://gitter.im/SlimCluster/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)
[![GitHub license](https://img.shields.io/github/license/zarusz/SlimCluster)](https://github.com/zarusz/SlimCluster/blob/master/LICENSE)
[![Build](https://github.com/zarusz/SlimCluster/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/zarusz/SlimCluster/actions/workflows/build.yml)

## Roadmap

> This a relatively new project!

The path to a stable production release:

- :white_check_mark: Step 1: Implement the SWIM membership over UDP + sample.
- :white_check_mark: Step 2: Documentation on Raft consensus.
- :white_check_mark: Step 3: Implement the Raft over TCP/UDP + sample.
- :white_large_square: Step 4: Documentation on SWIM membership.
- :white_large_square: Step 5: Other extensions and flavor.

## Docs

- [Introduction](docs/intro.md)

## Packages

| Name                                | Description                                | NuGet                                                                                                                                              |
| ----------------------------------- | ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SlimCluster`                       | The core cluster interfaces                | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.svg)](https://www.nuget.org/packages/SlimCluster)                                             |
| **Core abstractions**               |                                            |                                                                                                                                                    |
| `SlimCluster.Membership`            | The membership core interfaces             | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Membership.svg)](https://www.nuget.org/packages/SlimCluster.Membership)                       |
| `SlimCluster.Serialization`         | The core message serialization interfaces  | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Serialization.svg)](https://www.nuget.org/packages/SlimCluster.Serialization)                 |
| `SlimCluster.Transport`             | The core transport interfaces              | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Transport.svg)](https://www.nuget.org/packages/SlimCluster.Transport)                         |
| `SlimCluster.Persistence`           | The core node state persistence interfaces | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Persistence.svg)](https://www.nuget.org/packages/SlimCluster.Persistence)                     |
| **Plugins**                         |                                            |                                                                                                                                                    |
| `SlimCluster.Consensus.Raft`        | Raft consensus algorithm implementation    | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Consensus.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Consensus.Raft)               |
| `SlimCluster.Membership.Swim`       | SWIM membership algorithm implementation   | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Membership.Swim.svg)](https://www.nuget.org/packages/SlimCluster.Membership.Swim)             |
| `SlimCluster.Serialization.Json`    | JSON message serialization plugin          | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Serialization.Json.svg)](https://www.nuget.org/packages/SlimCluster.Serialization.Json)       |
| `SlimCluster.Transport.Ip`          | IP protocol transport plugin               | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Transport.Ip.svg)](https://www.nuget.org/packages/SlimCluster.Transport.Ip)                   |
| `SlimCluster.Persistence.LocalFile` | Persists node state into a local JSON file | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Persistence.LocalFile.svg)](https://www.nuget.org/packages/SlimCluster.Persistence.LocalFile) |
| `SlimCluster.AspNetCore`            | ASP.NET request routing to Leader node     | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.AspNetCore.svg)](https://www.nuget.org/packages/SlimCluster.AspNetCore)                       |

## Samples

Check out the [Samples](src/Samples/) folder on how to get started.

### Example usage

Setup membership discovery using the SWIM algorithm and consensus using Raft algorithm:

```cs
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

    // Protocol messages (and logs/commands) will be serialized using JSON
    cfg.AddJsonSerialization();

    // Cluster state will saved into the local json file in between node restarts
    cfg.AddPersistenceUsingLocalFile("cluster-state.json");

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

// Requires packages: SlimCluster.Membership.Swim, SlimCluster.Consensus.Raft, SlimCluster.Serialization.Json, SlimCluster.Transport.Ip, SlimCluster.Persistence.LocalFile, SlimCluster.AspNetCore
```

Then somewhere in the micro-service, the [`ICluster`](src/SlimCluster/ICluster.cs) can be used:

```cs
// Injected, this will be a singleton representing the cluster the service instances form.
ICluster cluster;

// Gives the current leader
INode? leader = cluster.LeaderNode;

// Gives the node representing current node
INode self = cluster.SelfNode;

// Provides a snapshot collection of the current nodes discovered and alive/healthy forming the cluster
IEnumerable<INode> nodes = cluster.Nodes;

// Provides a snapshot collection of the current nodes discovered and alive/healthy forming the cluster excluding self
IEnumerable<INode> otherNodes = cluster.OtherNodes;
```

The [`IClusterMembership`](src/SlimCluster.Membership/IClusterMembership.cs) can be used to understand membership changes:

```cs
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
```

## Architecture

- The service references SlimCluser NuGet packages and configures MSDI.
- Nodes (service instances) are communicating over UDP/IP and exchange protocol messages (SWIM and Raft).
- Cluster membership (nodes that form the cluster) is managed (SWIM).
- Cluster leader is elected at the beginning and in the event of failure (Raft).
- Logs (commands that chage state machine state) are replicated from leader to followers (Raft).
- State Machine in each Node gets logs (commands) applied which have been replicated to majority of nodes (Raft).
- Clients interact with the Cluster (state mutating operations are executed to Leader or Followers for reads) - depends on the use case.

![SlimCluster architecture](docs/images/SlimCluster.jpg)

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Build

```cmd
cd src
dotnet build
dotnet pack --output ../dist
```

NuGet packaged end up in `dist` folder

## Testing

To run tests you need to update the respective `appsettings.json` to match your cloud infrastructure or local infrastructure.

Run all tests:

```cmd
dotnet test
```

Run all tests except integration tests which require local/cloud infrastructure:

```cmd
dotnet test --filter Category!=Integration
```