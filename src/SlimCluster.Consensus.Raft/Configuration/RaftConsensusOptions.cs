namespace SlimCluster.Consensus.Raft;

using SlimCluster.Serialization;

public class RaftConsensusOptions
{
    /// <summary>
    /// Minimum election timeout min
    /// </summary>
    public TimeSpan ElectionTimeoutMin { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>
    /// Minimum election timeout max
    /// </summary>
    public TimeSpan ElectionTimeoutMax { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout after which a leader is considered dead (unless ApppendEntriesRequest arrives).
    /// </summary>
    public TimeSpan LeaderTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Time after which an empty AppendEntriesRequest must be sent to prevent Leader timout from happening.
    /// </summary>
    public TimeSpan LeaderPingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The stable IDs of all Raft voters, including the local node.
    /// Every voter must use the same list. Leave empty to bootstrap from discovered members.
    /// </summary>
    public string[] VoterIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Expected voter count when bootstrapping from discovery. With explicit voter IDs, this can be unset.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// The service type that should be resolved from MSDI for Log Entry serialization.
    /// </summary>
    public Type LogSerializerType { get; set; } = typeof(ISerializer);

    /// <summary>
    /// The timeout for a leader to process the request.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of snapshot bytes sent in a single InstallSnapshot request.
    /// </summary>
    public int SnapshotChunkSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum number of snapshot bytes a follower will buffer while installing a snapshot.
    /// </summary>
    public int MaxInstallSnapshotBytes { get; set; } = 64 * 1024 * 1024;
}
