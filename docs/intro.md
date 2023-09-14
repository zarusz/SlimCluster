# Introduction to SlimCluster <!-- omit in toc -->

- [About](#about)
- [Configuration](#configuration)
- [Transport](#transport)
  - [IP / UDP Transport](#ip--udp-transport)
- [Serialization](#serialization)
  - [JSON Serialization](#json-serialization)
- [Cluster Persistence](#cluster-persistence)
  - [Local File Persistence](#local-file-persistence)
- [Cluster Membership](#cluster-membership)
  - [SWIM Membership](#swim-membership)
  - [Static Membership](#static-membership)
- [Cluster Consensus](#cluster-consensus)
  - [Raft Consensus](#raft-consensus)
    - [Logs](#logs)
    - [Logs Compaction](#logs-compaction)
    - [Logs Storage](#logs-storage)
    - [State Machine](#state-machine)
    - [Configuration Parameters](#configuration-parameters)
  - [Leader Request Delegation ASP.NET Core Middleware](#leader-request-delegation-aspnet-core-middleware)

# About

SlimCluster is a library aimed to help with building NET services that need to be clustered (share state and need to coordinate work).
Such clustered services can be hosted in Kubernetes, Azure App Service, AWS, GCP, containers or IaaS - does not matter, as long as the Nodes have access to the same network and IP protocol.

The goal for SlimCluster is to provide all the relevant clustering features, (slim) abstraction over a cluster representation and implement algorithms that you can readily use without having to reimplement it. That way buiding a clustered service becomes easy, and the developer can focus on the business problem at hand.

Think about building the next distributed stream processing system (Flink), database (MongoDb) or messaging system (Kafka) in .NET. All of these have to solve a common problem - the need to know and maintain the cluster members, coordinate work, replicate state and elect the leader for the relevant shard/partition. SlimCluster is there to help with the foundations.

# Configuration

The SlimCluster configuration consists of adding it to the MSDI container using the `services.AddSlimCluster(cfg => {})` method:

```cs
// IServiceCollection services;

services.AddSlimCluster(cfg =>
{
    cfg.ClusterId = "MyCluster";
    // This will use the machine name, in Kubernetes this will be the pod name
    cfg.NodeId = Environment.MachineName;

    // Plugin: Transport will be over UDP/IP
    cfg.AddIpTransport(opts =>
    {
        opts.Port = 60001; // Any available port can be used, this value is default
        opts.MulticastGroupAddress = "239.1.1.1" // Any valid multicast group can be used, this value is default
    });

    // Plugin: Protocol messages (and logs/commands) will be serialized using JSON
    cfg.AddJsonSerialization();

    // Plugin: Cluster state will saved into the local json file in between node restarts
    cfg.AddPersistenceUsingLocalFile("cluster-state.json");

    // Other plugins: membership, consensus, tools
});
```

As part of the configuration we can set:

- What is the ClusterId (logical identifier) and name of the Node.
- What transport will be used (in the above example IP protocol).
- What serialization will be used for the protocol messages (membership, consensus, etc).
- How should the cluster state be persisted in between node instance restarts (this is optional).
- What membership algorithm to use (SWIM).
- What consensus algorithm to use (Raft).
- What other plugins to add.

SlimCluster registers as an `IHostedService` that works with MS Hosting abstraction.
This ensures the cluster starts when any [NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) compatible runtime starts (ASP.NET Core application).

# Transport

Package: [SlimCluster.Transport](https://www.nuget.org/packages/SlimCluster.Transport)

The transport allows the Nodes in the cluster to exchange protocol messages.

Currently the only possible transport option is IP protocol, where the inter-Node communication happens using UDP sockets.

## IP / UDP Transport

Package: [SlimCluster.Transport.Ip](https://www.nuget.org/packages/SlimCluster.Transport.Ip)

Configures SlimCluster to use the IP/UDP protocol for inter-node communication.

```cs
cfg.AddIpTransport(opts =>
{
    opts.Port = 60001;
    opts.MulticastGroupAddress = "239.1.1.1";
});
```

By design the various plugins that are added to SlimCluster share the same IP port.
The messages are multiplexed into the same Node endpoint, and then routed to the relevant plugin/layer that is able to handle the messages.
Thanks to this SC requires only one port.
In this scenario consensus (Raft) and membership (SWIM) protocol messages go through the same UDP port.

![](images/SlimCluster.jpg)

The `MulticastGroupAddress` property allows to set the multicast group used in the initial member discovery.
In the future there will be also option to avoid multicast groups.

# Serialization

Package: [SlimCluster.Serialization](https://www.nuget.org/packages/SlimCluster.Serialization)

Serialization is needed for protocol messages for inter-Node communication.

Currently the only possible serialization option is JSON.
In the near future there are plans to add [ProtoBuf](https://github.com/protocolbuffers/protobuf) binary serialization for performance and payload size optimization.

To write a custom serializer, simply provide an implementation for [ISerializer](../src/SlimCluster.Serialization/ISerializer.cs) in the MSDI container.

## JSON Serialization

Package: [SlimCluster.Serialization.Json](https://www.nuget.org/packages/SlimCluster.Serialization.Json)

```cs
// Plugin: Protocol messages (and logs/commands) will be serialized using JSON
cfg.AddJsonSerialization();
```

# Cluster Persistence

Package: [SlimCluster.Persistence](https://www.nuget.org/packages/SlimCluster.Persistence)

The Node (service instance) needs to persist the minimal cluster state in between instance restarts (or crushes) to quickly catch up with the other Nodes in the cluster.
Depending on the plugins and configuration used, such state might include:

- Last known member list
- Last known leader
- Algorithm state (Raft, SWIM)

While this is not required, it allows the restarted (or crushed) Node to catch up faster with the other members in the Cluster, and converge into the correct cluster state.

Currently the only possible option is LocalFile which stores the state in an local JSON file.
In the near future there are plans to add some connectors to Cloud Provider storage offerings (Azure, AWS, GCP).

To write a custom state persitence strategy, simply provide an implementation for [IClusterPersistenceService](../src/SlimCluster.Persistence/IClusterPersistenceService.cs) in the MSDI container. The service implementation needs to collect the state from all the container managed [IDurableComponent](../src/SlimCluster.Persistence/IDurableComponent.cs) (inject `IEnumerable<IDurableComponent>`).

## Local File Persistence

Package: [SlimCluster.Persistence.LocalFile](https://www.nuget.org/packages/SlimCluster.Persistence.LocalFile)

```cs
// Plugin: Cluster state will saved into the local json file in between node restarts
cfg.AddPersistenceUsingLocalFile("cluster-state.json");
```

# Cluster Membership

Package: [SlimCluster.Membership](https://www.nuget.org/packages/SlimCluster.Membership)

Cluster membership is all about knowing what members (nodes) form the cluster:

- which nodes are alive, healthy, and what is their address (IP/Port),
- running health checks to understand if members are reliable,
- observe members joining or leaving a cluster,
- keeping a list of all the active members.

The [IClusterMembership](../src/SlimCluster.Membership/IClusterMembership.cs) (can be injected) allows to:

- understand the current (self) member information,
- understand what other members form the cluster (Id, Address).

SlimCluster has the [SWIM membership](#swim-membership) algorithm implemented as one of the options.

## SWIM Membership

Package: [SlimCluster.Membership.Swim](https://www.nuget.org/packages/SlimCluster.Membership.Swim)

ToDo

## Static Membership

Package: [SlimCluster.Membership](https://www.nuget.org/packages/SlimCluster.Membership)

If the members of the cluster are known then you do not need to run a membership algorithm:

- The node names (or IP address) are known and fixed,
- Perhaps there is an external framework (or middleware) that tracks the node names (or IP address).

ToDo:

# Cluster Consensus

Package: [SlimCluster.Consensus](https://www.nuget.org/packages/SlimCluster.Consensus)

Consensus allows to coordinate the nodes that form the cluster. It helps to manage a common understanding of reality (cluster state) and replicate that state in case of node failures. Consensus typically requres a node to be designated as the leader that performs the coordination of work and state replication.

## Raft Consensus

Package: [SlimCluster.Consensus.Raft](https://www.nuget.org/packages/SlimCluster.Consensus.Raft)

Raft consensus algorithm is one of the newer algorithms that has become popular due to its simplicity and flexibility.
The [Raft paper](https://raft.github.io/raft.pdf) is an excelent source to undersand more details about the algorithm, and the parameters that can be fine tuned in SlimCluster.

At a high level the Raft consensus is:

- Storing and replicating logs to all cluster nodes (Replicated Logs).
- Electing a leader node that is responsible for log replication to follower nodes (Leader Election).
- Leader applies the logs to its own state machine (and followers) when logs have been replicated to the majority of nodes (Replicated State Machine).
- The state machine represents cluster state that is observed by the leader & majority.
- Logs represent commands that mutate cluster state.

From the SlimCluster perspective, the logs (commands) and state machine can be anything that is relevant to the application domain problem in question.
This is the part that you have to customize to meet the particular use case (examples further on).

To add the Raft plugin register it:

```cs
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
```

### Logs

Logs represent operations (commands) that perform state change within the [replicated state machine](#state-machine).
Think about them as commands that mutate state, which need to be executed in order to reproduce the current state of the state machine.

In Raft algorithm each log entry has an:

- Index that represents its position and defines the order of when it happend (starts from 1)
- Term that represents a virtual integer time that is advanced with every leader election (starts from 1).

In the case of a distributed counter such commands could be `IncrementCounterCommand`, `DecrementCounterCommand`, `ResetCounterCommand`, while the state (state machine) represents the current counter value as observed by the leader (and eventually other members of the cluster).

### Logs Compaction

Over time the logs grow in size and in certain moments a snapshot at index `N` is being taken to capture the state machine value (up to `N`)
With that `N-1` logs are removed to save on space. That process is called log compaction (or snapshoting).

Thanks to this, between node restarts the cluster leader can send the new joined follower the most recent state snapshot, along with the new logs that happened after that. This allows new followers to quickly catch up to the cluster state.

> Log compaction is not yet supported, but will come soon.

### Logs Storage

The Raft implementation requires a registered `ILogRepository` service implementation to represent storage stategy.

Currently, the only option is `InMemoryLogRepository`, which means the logs are stored in memory.
The in-memory strategy can be set up like this:

```cs
services.AddSingleton<ILogRepository, InMemoryLogRepository>(); // For now, store the logs in memory only
```

What is important that the logs (e.g. custom commands `IncrementCounterCommand`) need to be serializable by the [chosen serialization plugin](#serialization).
Alternatively, if you want to serialize the custom commands (app specific commands) with a different serializer then, you can set the type to look up in the MSDI:

```cs
cfg.AddRaftConsensus(opts =>
{
    // Can set a different log serializer, by default ISerializer is used (in our setup its JSON)
    opts.LogSerializerType = typeof(JsonSerializer);
});
```

### State Machine

Raft relies on a state machine which is able to execute logs (commands) and hence produce state.
The state machine is being evaluated on every node on the cluster (not only the leader node).
All node state machines eventually end up in the same state across leader and follower nodes.

Leader is the one that decides up to what log index should be applied against the state machine.
A log at index `N` is applied onto the state machine if the log at index `N` (and all before it) hve been replicated by the leader to a majority of nodes in the cluster.

The state machine represents your custom domain problem that, and works with the custom logs (commands) that are relevant for the state machine.
For example if we are building a distributed counter, then the state machine is able to handle IncrementCounterCommand, DecrementCounterCommand, etc. The evaluation of each command, causes the counter increments, decrements.

The Raft implementation required an implementation of the [`IStateMachine`](../src/SlimCluster.Consensus.Raft/StateMachine/IStateMachine.cs) to be registerd in MSDI.

```cs
builder.Services.AddSingleton<IStateMachine, CounterStateMachine>(); // This is app specific machine that implements a distributed counter
```

The implementation for the `CounterStateMachine` could look like this:

```cs
public class CounterStateMachine : IStateMachine
{
    private int _index = 0;
    private int _counter = 0;

    public int CurrentIndex => _index;

    /// <summary>
    /// The counter value
    /// </summary>
    public int Counter => _counter;

    public Task<object?> Apply(object command, int index)
    {
        // Note: This is thread safe - there is ever going to be only one task at a time calling Apply

        if (_index + 1 != index)
        {
            throw new InvalidOperationException($"The State Machine can only apply next command at index ${_index + 1}");
        }

        int? result = command switch
        {
            IncrementCounterCommand => ++_counter,
            DecrementCounterCommand => --_counter,
            ResetCounterCommand => _counter = 0,
            _ => throw new NotImplementedException($"The command type ${command?.GetType().Name} is not supported")
        };

        _index = index;

        return Task.FromResult<object?>(result);
    }
}
```

### Configuration Parameters

Raft configuration parameters refer to the properties inside the `opts` object (of type [RaftConsensusOptions](../src/SlimCluster.Consensus.Raft/Configuration/RaftConsensusOptions.cs)):

```cs
// Setup Raft Cluster Consensus
cfg.AddRaftConsensus(opts =>
{
    opts.NodeCount = 3;

    // Use custom values or remove and use defaults
    opts.LeaderTimeout = TimeSpan.FromSeconds(5);
    opts.LeaderPingInterval = TimeSpan.FromSeconds(2);
    opts.ElectionTimeoutMin = TimeSpan.FromSeconds(3);
    opts.ElectionTimeoutMax = TimeSpan.FromSeconds(6);
});
```

- `NodeCount` sets the expected node count of the cluster. This is needed to be able to calculate the majority of nodes.
- `LeaderTimeout` the time after which the leader is considered crashed/gone/unreliable/failed when no messages arrive from the leader to the follower node.
- `LeaderPingInterval` the maximum round trip time the leader sends AppendEntriesRequest and until it has to get the AppendEntriesResponse back from the follower. This has to be big enough to allow for the network round trip, as well as for the leader and follower to process the message. This time should be significantly smaller than `LeaderTimeout`.
- `ElectionTimeoutMin` the minimum time at which the election could time out if the candidate did not collect a majority of votes.
- `ElectionTimeoutMax` thge maximum time at which the eelection could time out if the candidate did not collect a majority of votes. Each new election started by node `N` initalizes its election timeout to a random time span between the min and max values.

## Leader Request Delegation ASP.NET Core Middleware

Package: [SlimCluster.AspNetCore](https://www.nuget.org/packages/SlimCluster.AspNetCore)

ToDo
