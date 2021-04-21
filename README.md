# SlimCluster

SlimCluster has the [Raft](https://raft.github.io/raft.pdf) distributed consensus algorithm implemented in .NET.
Additionaly, it implements a [SWIM](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) cluster membership list (where nodes come online and go offline/die).

* Membership list is required to maintain what instances constitute a cluster.
* Raft consensus helps propagata state across the micro-service instances and ensure there is a leader instance performing the coordination of work.

The library goal is to provide a common groundwork for coordination and consensus of your distributed micro-service instances. 
With that the developer can focus on the business problem at hand.
The library promisses to have a friendly API and pluggable architecture.

The strategic aim for SlimCluster is to implement other algorithms to make distributed .NET micro-services easier and not require one to pull in a load of other 3rd party libraries or products.

> This is work in progress! 

[![Gitter](https://badges.gitter.im/SlimCluster/community.svg)](https://gitter.im/SlimCluster/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)
[![GitHub license](https://img.shields.io/github/license/zarusz/SlimCluster)](https://github.com/zarusz/SlimCluster/blob/master/LICENSE)

| Branch  | Build Status                                                                                                                                                                  |
|---------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| master  | [![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s/branch/master?svg=true)](https://ci.appveyor.com/project/zarusz/slimmessagebus/branch/master)   |
| develop | [![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s/branch/develop?svg=true)](https://ci.appveyor.com/project/zarusz/slimmessagebus/branch/develop) |
| other   | [![Build status](https://ci.appveyor.com/api/projects/status/6ppr19du717spq3s?svg=true)](https://ci.appveyor.com/project/zarusz/slimmessagebus)                               |


## Packages

| Name                                        | Descripton                                                                                                          | NuGet                                                                                                                                                                      |
|---------------------------------------------|---------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `SlimCluster`                               | The core cluster interfaces                                                                                         | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Strategy.Raft`                 | The Raft algorithm implementation                                                                                   | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Strategy.Raft.Transport.Redis` | Raft RPC implemented with Redis Pub/Sub                                                                             | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Strategy.Raft.Transport.Tcp`   | Raft RPC implemented with TCP                                                                                       | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Membership`                    | The membership core interfaces                                                                                      | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Membership.Swim`               | The SWIM membership algorithm implementation                                                                        | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |
| `SlimCluster.Membership.Redis`              | The membership implementation based on Redis Pub/Sub                                                                | [![NuGet](https://img.shields.io/nuget/v/SlimCluster.Strategy.Raft.svg)](https://www.nuget.org/packages/SlimCluster.Strategy.Raft)                                         |


## Samples

Check out the [Samples](src/Samples/) folder.

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

To run tests you need to update the respective `appsettings.json` to match your own cloud infrstructure or local infrastructure.

Run all tests:
```cmd
dotnet test
```

Run all tests except  integration tests which require local/cloud infrastructure:
```cmd
dotnet test --filter Category!=Integration
```