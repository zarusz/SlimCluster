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
- :white_large_square: Step 2: Documentation on SWIM membership.
- :white_check_mark: Step 3: Implement the Raft over TCP/UDP + sample.
- :white_large_square: Step 4: Documentation on Raft consensus.
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

@[:cs](src/Samples/SlimCluster.Samples.Service/Program.cs,ExampleStartup)

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

@[:cs](src/Samples/SlimCluster.Samples.Service/MainApp.cs,ExampleMembershipChanges)

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
