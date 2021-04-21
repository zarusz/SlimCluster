# SlimCluster

SlimCluster has the [Raft](https://raft.github.io/raft.pdf) distributed consensus algorithm implemented in .NET.
Additionally, it implements the [SWIM](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) cluster membership list (where nodes join and leaves/die).

* Membership list is required to maintain what micro-service instances (nodes) constitute a cluster.
* Raft consensus helps propagate state across the micro-service instances and ensures there is a designated leader instance performing the coordination of work.

The library goal is to provide a common groundwork for coordination and consensus of your distributed micro-service instances.
With that, the developer can focus on the business problem at hand.
The library promises to have a friendly API and pluggable architecture.

The strategic aim for SlimCluster is to implement other algorithms to make distributed .NET micro-services easier and not require one to pull in a load of other 3rd party libraries or products.

[![Gitter](https://badges.gitter.im/SlimCluster/community.svg)](https://gitter.im/SlimCluster/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)
[![GitHub license](https://img.shields.io/github/license/zarusz/SlimCluster)](https://github.com/zarusz/SlimCluster/blob/master/LICENSE)
[![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s/branch/master?svg=true&passingText=master%20OK&pendingText=master%20pending&failingText=master%20failL)](https://ci.appveyor.com/project/zarusz/slimcluster/branch/master)
[![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s/branch/develop?svg=true&passingText=develop%20OK&pendingText=develop%20pending&failingText=develop%20fail)](https://ci.appveyor.com/project/zarusz/slimcluster/branch/develop)
[![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s?svg=true&passingText=other%20OK&pendingText=other%20pending&failingText=other%20fail)](https://ci.appveyor.com/project/zarusz/slimcluster)

## Roadmap

> This is a new project and still a work in progress!

The path to a stable production release:

* Step 1: Implement the SWIM membership over UDP + sample (70% complete).
* Step 2: Documentation.
* Step 3: Implement the Raft over TCP/UDP + sample.
* Step 4: Documentation.
* Step 5: Other extensions and flavor (Redis Pub/Sub).

## Packages

| Name                                          | Description                                          | NuGet                                                                                                                              |
| --------------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `SlimCluster`                                 | The core cluster interfaces                          | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Strategy.Raft`                   | The Raft algorithm implementation                    | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Strategy.Raft.Transport.Tcp`     | Raft RPC implemented with TCP                        | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Strategy.Raft.Transport.Redis`   | Raft RPC implemented with Redis Pub/Sub              | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Membership`                      | The membership core interfaces                       | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Membership.Swim.Transport.Udp`   | The SWIM membership algorithm implementation         | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |
| `SlimCluster.Membership.Swim.Transport.Redis` | The membership implementation based on Redis Pub/Sub | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft) |

## Samples

Check out the [Samples](src/Samples/) folder.

## Example usage

Setup membership discovery using the SWIM algorithm:

```cs
// Assuming you're using Microsoft.Extensions.DependencyInjection
IServicesCollection services;

// We are setting up the SWIM membership algorithm for your micro-service instances
services.AddClusterMembership(opts => {
    opts.ClusterId = "MyMicroserviceCluster";
});

```

Then somewhere in the micro-service, you can inject the `IClusterMembership`:

```cs
// Injected, this will be a singleton
IClusterMembership cluster;

// Provides a snapshot collection of the current instances discovered and alive/healthy:
cluster.Members 

// Allows to get notifications when an new instance joines or leaves (dies):
cluster.MemberJoined += (sender, e) => { /* e.Node and e.Timestamp */ };
cluster.MemberLeft += (sender, e) => { /* e.Node and e.Timestamp */ };

```

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

Run all tests except  integration tests which require local/cloud infrastructure:
```cmd
dotnet test --filter Category!=Integration
```
