# Samples

## SlimCluster.Samples.Service

This sample showcases a simple .NET Service (exposing a distributed counter API) that can be built into a docker image and then deployed to Kubernetes (minikube or docker desktop).
The app is running in 3 instances (pods) on Kubernetes. The instances are forming a cluster.

The Kubernetes StatefulSet gives each pod a stable node ID (`sc-service-0`, `sc-service-1`, or `sc-service-2`). Each pod uses the same configured voter list. When running outside Kubernetes, the sample defaults to one voter using the machine name. To run on multiple hosts, configure the same `RaftVoterIds` list of machine names on each host.

> The scripts were tested on Docker Desktop with Minikube

To run the sample:

1. Navigate to `SlimCluster.Samples.Service` folder.

2. Build docker & publish image locally:

```txt
./Docker-BuildSample.ps1
```

3. Deploy to Kubernetes:

```txt
./Kube-ApplySample.ps1
```

4. Check if the pods are running:

```txt
kubectl get pods
```

As per the `deployment.yaml` file, there should be 3 pods running:

```txt
PS D:\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl get pods
NAME                          READY   STATUS    RESTARTS   AGE
sc-service-0   1/1     Running   0          2s
sc-service-1   1/1     Running   0          2s
sc-service-2   1/1     Running   0          2s
```

5. When checking the logs for the first pod:

```txt
kubectl logs -f sc-service-0
```

The result could look like this:

```txt
PS D:\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl logs -f sc-service-0
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 0
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      Starting service...
info: SlimCluster.Host.ClusterControl[0]
      Node is starting...
info: SlimCluster.Transport.Ip.IPMessageEndpoint[0]
      Joining multicast group 239.1.1.1
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Cluster membership protocol (SWIM) starting for sc-service-0 ...
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Sending NodeJoinedMessage for node sc-service-0 to the multicast group 239.1.1.1:60001
info: SlimCluster.Host.ClusterControl[0]
      Node started
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: /app
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      The member sc-service-1 joined
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-1@10.1.0.177:60001, sc-service-0@Unknown
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-1 joined at 10.1.0.177:60001
info: SlimCluster.Membership.Swim.SwimMember[0]
      Updated member sc-service-0 observed address to 10.1.0.176:60001
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      The member sc-service-2 joined
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-1@10.1.0.177:60001, sc-service-2@10.1.0.178:60001, sc-service-0@10.1.0.176:60001
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-2 joined at 10.1.0.178:60001
info: SlimCluster.Consensus.Raft.RaftNode[0]
      sc-service-1@10.1.0.177:60001: Node indicated there is a higher term 1
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 1
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Sending RequestVoteResponse(Term=1,VoteGranted=True) to node sc-service-1@10.1.0.177:60001
info: SlimCluster.Consensus.Raft.RaftFollowerState[0]
      New leader is sc-service-1@10.1.0.177:60001 for term 1
```

The second node `sc-service-1` (Kubernetes pod) has been elected the leader for term 1.

6. If the 2nd pod where to be deleted, the other nodes should detect this failure:

```txt
kubectl delete pod sc-service-1
```

The remaining two voters can elect a leader and continue committing writes. Kubernetes recreates the deleted pod with the same node ID.
