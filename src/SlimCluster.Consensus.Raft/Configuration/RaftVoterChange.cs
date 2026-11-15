namespace SlimCluster.Consensus.Raft;

/// <summary>A Raft configuration entry. A joint entry requires a majority of each set.</summary>
public sealed record RaftVoterChange(string[] OldVoterIds, string[] NewVoterIds, bool IsJoint);
