# SlimCluster roadmap

## P0 · Raft safety

- [x] **Use a fixed Raft voter set.** Configure the same voter IDs on every node. Use those IDs for elections and commit acknowledgments, and use SWIM to discover peers and track reachability. Preserve each vote for its full term. Cover a five-node cluster split into groups of three and two, including recovery when connectivity returns. [Split-brain report #29](https://github.com/zarusz/SlimCluster/issues/29)
- [x] **Bound follower commits by the verified log prefix.** Advance the commit index only through the last entry verified by the current `AppendEntries` request. Test heartbeats and partial appends against a divergent local suffix.
- [x] **Bootstrap voters from SWIM.** Discover the initial voter IDs using the expected node count, publish the roster through an atomic shared store, and wait for each voter to persist it before elections.

## P1 · Sample and recovery

- **Fix the counter sample across elections.** Handle the Raft no-op entries that advance the log index between application commands. Smoke-test the first HTTP increment after an election and another after failover.
- **Make Raft storage durable and finish snapshot compaction.** Save the term, vote, and log across crashes. Capture snapshot bytes with their exact index and term. Size chunks for the serialized UDP payload. Test restart and lagging-follower recovery over real UDP. [Log compaction #14](https://github.com/zarusz/SlimCluster/issues/14)
- [x] **Support Raft voter reconfiguration.** Replicate configuration changes through Raft. Catch up new voters before promotion and require a majority of both the old and new configurations during the transition.
- [x] **Automatically replace absent voters.** Use a configurable SWIM observation period, confirm a healthy standby learner and current Raft quorum, and reuse joint consensus for each one-for-one replacement.

## P2 · Deployment and operations

- **Add unicast seed discovery.** Let SWIM discover peers without multicast and demonstrate it in the Kubernetes sample. [Seed discovery #7](https://github.com/zarusz/SlimCluster/issues/7)
- **Expose quorum-aware readiness.** Give applications a way to observe when a leader can still commit writes and return HTTP 503 during quorum loss. Document the consistency of reads served during a partition. [State observers #15](https://github.com/zarusz/SlimCluster/issues/15)

## Build maintenance

- Update the SourceLink build dependency to resolve the advisory affecting its transitive `Microsoft.Build.Tasks.Git` 8.0.0 dependency. [Microsoft advisory](https://github.com/advisories/GHSA-23fw-v26w-5fgq)
