namespace SlimCluster.Consensus.Raft;

internal sealed class RaftVoterConfiguration
{
    private readonly HashSet<string> _ids;

    public int Majority => _ids.Count / 2 + 1;
    public string[] GetIds() => _ids.Order(StringComparer.Ordinal).ToArray();

    public RaftVoterConfiguration(RaftConsensusOptions options, string localId)
        : this(options.VoterIds, options.NodeCount, localId)
    {
    }

    public RaftVoterConfiguration(string[] ids, int expectedCount, string localId, bool allowLearner = false)
    {
        if (ids == null || ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Raft requires a nonempty list of voter IDs.", nameof(ids));
        }

        _ids = new HashSet<string>(ids, StringComparer.Ordinal);
        if (_ids.Count != ids.Length || (!allowLearner && !_ids.Contains(localId)))
        {
            throw new ArgumentException("Raft voter IDs must be unique and include the local node ID.", nameof(ids));
        }

        if (expectedCount != 0 && expectedCount != _ids.Count)
        {
            throw new ArgumentException("Raft NodeCount must match the number of voter IDs when specified.", nameof(expectedCount));
        }
    }

    public bool Contains(string id) => _ids.Contains(id);

    internal static string[] ValidateVoterIds(IEnumerable<string> voterIds)
    {
        var ids = voterIds.Order(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException("Raft requires a nonempty list of unique voter IDs.", nameof(voterIds));
        }

        return ids;
    }
}
