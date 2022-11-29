﻿namespace SlimCluster.Consensus.Raft;
public interface IRaftTransport
{
    Task<RequestVoteResponse> RequestVote(RequestVoteRequest request, IAddress node);
    Task<AppendEntriesResponse> AppendEntries(AppendEntriesRequest request, IAddress node);
    Task<InstallSnapshotResponse> InstalSnapshot(InstallSnapshotRequest request, IAddress node);
}
