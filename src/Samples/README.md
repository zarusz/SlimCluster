# Samples

## SlimCluster.Samples.Service

This sample showcases a simple .NET Service (exposing a distributed counter API) that can be built into a docker image and then deployed to Kubernetes (minikube or docker desktop).
The app is running in 3 instances (pods) on Kubernetes. The instances are forming a cluster.

The node name is assigned from the machine name (`Environment.MachineName`), this is to have the node name align with pod names in Kubernetes.

> The scripts were tests on Docker Desktop with Minikube

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
PS C:\Users\tomasz\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl get pods
NAME                            READY   STATUS    RESTARTS   AGE
sc-service-b7757bf68-6hh42   1/1     Running   0          4s
sc-service-b7757bf68-fv688   1/1     Running   0          4s
sc-service-b7757bf68-xtvqh   1/1     Running   0          4s
```

5. When checking the logs for the first pod:

```txt
kubectl logs -f sc-service-b7757bf68-fv688
```

The result could look like this:

```txt
PS C:\Users\tomasz\dev\mygithub\SlimCluster\src\Samples\SlimCluster.Samples.Service> kubectl logs -f sc-service-b7757bf68-6hh42
info: SlimCluster.Samples.Service.MainApp[0]
      Node is starting...
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Cluster membership protocol (SWIM) starting for sc-service-b7757bf68-6hh42...
info: SlimCluster.Membership.Swim.MessageEndpoint[0]
      Recieve loop started. Node listening on 0.0.0.0:60001
dbug: SlimCluster.Membership.Swim.SwimFailureDetector[0]
      Started period 1 and timeout on 2022-11-27T08:58:51
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Cluster membership protocol (SWIM) started for sc-service-b7757bf68-6hh42
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Sending NodeJoinedMessage for node sc-service-b7757bf68-6hh42 on the multicast group 239.1.1.1:60001
info: SlimCluster.Samples.Service.MainApp[0]
      Node is running
info: SlimCluster.Membership.Swim.SwimMemberSelf[0]
      Updated observed address of self to 10.1.0.180:60001
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-b7757bf68-xtvqh joined at 10.1.0.182:60001
dbug: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Sending Welcome to sc-service-b7757bf68-xtvqh on 10.1.0.182:60001 with 1 known members (including self)
info: SlimCluster.Samples.Service.MainApp[0]
      The member sc-service-b7757bf68-xtvqh joined
info: SlimCluster.Samples.Service.MainApp[0]
      This node is aware of sc-service-b7757bf68-xtvqh/(10.1.0.182:60001), sc-service-b7757bf68-6hh42/(10.1.0.180:60001)
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-b7757bf68-fv688 joined at 10.1.0.181:60001
dbug: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Sending Welcome to sc-service-b7757bf68-fv688 on 10.1.0.181:60001 with 2 known members (including self)
info: SlimCluster.Samples.Service.MainApp[0]
      The member sc-service-b7757bf68-fv688 joined
info: SlimCluster.Samples.Service.MainApp[0]
      This node is aware of sc-service-b7757bf68-xtvqh/(10.1.0.182:60001), sc-service-b7757bf68-fv688/(10.1.0.181:60001), sc-service-b7757bf68-6hh42/(10.1.0.180:60001)
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Recieved starting list of nodes (1) from 10.1.0.181:60001
dbug: SlimCluster.Membership.Swim.SwimFailureDetector[0]
      Started period 2 and timeout on 2022-11-27T08:59:06
```

6. If the 2nd pod where to be deleted, the other nodes should detect this failure:

```txt
kubectl delete pod sc-service-b7757bf68-fv688
```

Then checking logs on first pod (`kubectl logs -f sc-service-b7757bf68-6hh42`):

```txt
info: SlimCluster.Membership.Swim.SwimMember[0]
      Member sc-service-b7757bf68-fv688 changes status to Faulted (previous Confirming)
info: SlimCluster.Membership.Swim.SwimClusterMembership[0]
      Node sc-service-b7757bf68-fv688 left at 10.1.0.181:60001
info: SlimCluster.Samples.Service.MainApp[0]
      The member sc-service-b7757bf68-fv688 left/faulted
info: SlimCluster.Samples.Service.MainApp[0]
      This node is aware of sc-service-b7757bf68-xtvqh/(10.1.0.182:60001), sc-service-b7757bf68-xdndp/(10.1.0.183:60001), sc-service-b7757bf68-6hh42/(10.1.0.180:60001)
info: SlimCluster.Membership.Swim.SwimFailureDetector[0]
      Node sc-service-b7757bf68-fv688 was declared as Faulted - ack message for ping (direct or indirect) did not arrive in time for period 10
```

> This sample uses Docker and Kubernetes, but this is matter of covenience (docker desktop with minikube). 
> The sample console app will also work if it were to be started directly on some host machines that are on the same network.