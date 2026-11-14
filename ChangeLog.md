# Changelog

## Unreleased

### Raft safety

- Elections and write commits require a majority of the configured voters, even when SWIM loses contact with peers. Nodes retain their vote for the full term.
- Followers apply only entries verified by the leader's current append request, preventing a heartbeat or partial append from committing a divergent log suffix.
- Client writes throw `TimeoutException` when the result cannot be confirmed before the deadline. A timed-out command may still commit later.

### Samples and documentation

- The Kubernetes counter sample now uses a StatefulSet with stable voter IDs. The sample can also run locally as a single voter.
- Added the [project roadmap](docs/Roadmap.md) and migration instructions for the [Kubernetes sample](src/Samples/README.md).

### Upgrade

Set the same `VoterIds` on every Raft node, including its own stable node ID. Preserve existing node IDs and persisted state, and coordinate the restart across all voters. See the [Raft upgrade guide](docs/intro.md#upgrading-an-existing-raft-cluster).
