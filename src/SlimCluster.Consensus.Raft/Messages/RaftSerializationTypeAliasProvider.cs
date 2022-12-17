namespace SlimCluster.Consensus.Raft;

using SlimCluster.Serialization;

internal class RaftSerializationTypeAliasProvider : ISerializationTypeAliasProvider
{
    public IReadOnlyDictionary<string, Type> GetTypeAliases() => new Dictionary<string, Type>
    {
        ["raft-ae"] = typeof(AppendEntriesRequest),
        ["raft-aer"] = typeof(AppendEntriesResponse),
        ["raft-is"] = typeof(InstallSnapshotRequest),
        ["raft-isr"] = typeof(InstallSnapshotResponse),
        ["raft-rv"] = typeof(RequestVoteRequest),
        ["raft-rvr"] = typeof(RequestVoteResponse),
    };
}