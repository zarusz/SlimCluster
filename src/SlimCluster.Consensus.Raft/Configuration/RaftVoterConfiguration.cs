namespace SlimCluster.Consensus.Raft;

internal sealed class RaftVoterConfiguration
{
    private readonly HashSet<string> _ids;

    public int Majority => _ids.Count / 2 + 1;

    public RaftVoterConfiguration(RaftConsensusOptions options, string localId)
    {
        var ids = options.VoterIds;
        if (ids == null || ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Raft requires a nonempty list of voter IDs.", nameof(options));
        }

        _ids = new HashSet<string>(ids, StringComparer.Ordinal);
        if (_ids.Count != ids.Length || !_ids.Contains(localId))
        {
            throw new ArgumentException("Raft voter IDs must be unique and include the local node ID.", nameof(options));
        }

        if (options.NodeCount != 0 && options.NodeCount != _ids.Count)
        {
            throw new ArgumentException("Raft NodeCount must match the number of voter IDs when specified.", nameof(options));
        }
    }

    public bool Contains(string id) => _ids.Contains(id);
}
