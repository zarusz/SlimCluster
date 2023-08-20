# Samples

## SlimCluster.Samples.Service

This sample showcases a simple .NET Service (exposing a distributed counter API) that can be built into a docker image and then deployed to Kubernetes (minikube or docker desktop).
The app is running in 3 instances (pods) on Kubernetes. The instances are forming a cluster.

The node name is assigned from the machine name (`Environment.MachineName`), this is to have the node name align with pod names in Kubernetes.

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

As per the `deployment.yml` file, there should be 3 pods running:

```txt
PS D:\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl get pods
NAME                          READY   STATUS    RESTARTS   AGE
sc-service-69bfd7f7b7-f428h   1/1     Running   0          2s
sc-service-69bfd7f7b7-gw6pp   1/1     Running   0          2s
sc-service-69bfd7f7b7-rmxbr   1/1     Running   0          2s
```

5. When checking the logs for the first pod:

```txt
kubectl logs -f sc-service-69bfd7f7b7-f428h
```

The result could look like this:

```txt
PS D:\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl logs -f sc-service-69bfd7f7b7-f428h
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 0
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      Starting service...
info: SlimCluster.Host.ClusterControl[0]
      Node is starting...
info: SlimCluster.Transport.Ip.IPMessageEndpoint[0]
      Joining multicast group 239.1.1.1
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Cluster membership protocol (SWIM) starting for sc-service-69bfd7f7b7-f428h ...
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Sending NodeJoinedMessage for node sc-service-69bfd7f7b7-f428h to the multicast group 239.1.1.1:60001
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
      The member sc-service-69bfd7f7b7-gw6pp joined
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001, sc-service-69bfd7f7b7-f428h@Unknown
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-69bfd7f7b7-gw6pp joined at 10.1.0.177:60001
info: SlimCluster.Membership.Swim.SwimMember[0]
      Updated member sc-service-69bfd7f7b7-f428h observed address to 10.1.0.176:60001
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      The member sc-service-69bfd7f7b7-rmxbr joined
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001, sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001, sc-service-69bfd7f7b7-f428h@10.1.0.176:60001
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-69bfd7f7b7-rmxbr joined at 10.1.0.178:60001
info: SlimCluster.Consensus.Raft.RaftNode[0]
      sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001: Node indicated there is a higher term 1
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 1
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Sending RequestVoteResponse(Term=1,VoteGranted=True) to node sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001
info: SlimCluster.Consensus.Raft.RaftFollowerState[0]
      New leader is sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001 for term 1
```

The second node `sc-service-69bfd7f7b7-gw6pp` (Kubernetes pod) has been elected the leader for term 1.

6. If the 2nd pod where to be deleted, the other nodes should detect this failure:

```txt
kubectl delete pod sc-service-69bfd7f7b7-gw6pp
```

Then checking logs on first pod again (`kubectl logs -f sc-service-69bfd7f7b7-gw6pp`):

```txt
info: SlimCluster.Consensus.Raft.RaftFollowerState[0]
      New leader is sc-service-69bfd7f7b7-gw6pp@10.1.0.177:60001 for term 1
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-69bfd7f7b7-gw6pp left at 10.1.0.177:60001
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      The member sc-service-69bfd7f7b7-gw6pp left/faulted
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001, sc-service-69bfd7f7b7-f428h@10.1.0.176:60001
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      The member sc-service-69bfd7f7b7-qps5s joined
info: SlimCluster.Samples.ConsoleApp.MainApp[0]
      This node is aware of sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001, sc-service-69bfd7f7b7-qps5s@10.1.0.179:60001, sc-service-69bfd7f7b7-f428h@10.1.0.176:60001
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-69bfd7f7b7-qps5s joined at 10.1.0.179:60001
info: SlimCluster.Consensus.Raft.RaftNode[0]
      sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001: Node indicated there is a higher term 2
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 2
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Sending RequestVoteResponse(Term=2,VoteGranted=True) to node sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001
info: SlimCluster.Consensus.Raft.RaftFollowerState[0]
      New leader is sc-service-69bfd7f7b7-rmxbr@10.1.0.178:60001 for term 2
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Sending RequestVoteResponse(Term=2,VoteGranted=False) to node sc-service-69bfd7f7b7-qps5s@10.1.0.179:60001
info: SlimCluster.Consensus.Raft.RaftNode[0]
      sc-service-69bfd7f7b7-qps5s@10.1.0.179:60001: Node indicated there is a higher term 3
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Becoming a follower for term 3
info: SlimCluster.Consensus.Raft.RaftNode[0]
      Sending RequestVoteResponse(Term=3,VoteGranted=True) to node sc-service-69bfd7f7b7-qps5s@10.1.0.179:60001
info: SlimCluster.Consensus.Raft.RaftFollowerState[0]
      New leader is sc-service-69bfd7f7b7-qps5s@10.1.0.179:60001 for term 3
```

> The sample uses Docker and Kubernetes, but this is matter of convenience (docker desktop with minikube).
> The sample app will also work if it were to be started directly on some host machines that are on the same network.
