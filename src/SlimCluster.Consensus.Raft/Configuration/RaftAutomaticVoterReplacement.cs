namespace SlimCluster.Consensus.Raft;

using SlimCluster.Membership;

internal sealed class RaftAutomaticVoterReplacement(TimeSpan delay, Func<IMember, bool>? admit)
{
    private readonly Dictionary<string, DateTimeOffset> _missingSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IAddress Address, DateTimeOffset Since, DateTimeOffset Joined)> _presentSince = new(StringComparer.Ordinal);

    public DateTimeOffset ProbeAgainAt { get; set; }

    public void Observe(RaftVoterView voters, IReadOnlyCollection<IMember> members, string selfId, DateTimeOffset now)
    {
        var present = members.Select(member => member.Node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _presentSince.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            _presentSince.Remove(id);
        }
        foreach (var member in members)
        {
            var id = member.Node.Id;
            if (!_presentSince.TryGetValue(id, out var observation)
                || !observation.Address.Equals(member.Node.Address) || observation.Joined != member.Joined)
            {
                _presentSince[id] = (member.Node.Address, now, member.Joined);
            }
        }

        foreach (var id in _missingSince.Keys.Where(id => present.Contains(id) || !voters.Contains(id)).ToArray())
        {
            _missingSince.Remove(id);
        }
        foreach (var id in voters.NewIds.Where(id => id != selfId && !present.Contains(id)))
        {
            _missingSince.TryAdd(id, now);
        }
    }

    public string? FindMissingVoter(DateTimeOffset now)
        => _missingSince.Keys.Where(id => IsMissing(id, now))
            .OrderBy(id => _missingSince[id])
            .ThenBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();

    public bool IsMissing(string id, DateTimeOffset now)
        => _missingSince.TryGetValue(id, out var since) && now - since >= delay;

    public bool IsEligibleLearner(IMember member, DateTimeOffset now)
        => _presentSince.TryGetValue(member.Node.Id, out var observation)
            && now - observation.Since >= delay
            && observation.Address.Equals(member.Node.Address)
            && (admit == null || admit(member));
}
