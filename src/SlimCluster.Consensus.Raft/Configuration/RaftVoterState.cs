namespace SlimCluster.Consensus.Raft;

using SlimCluster.Consensus.Raft.Logs;

internal sealed class RaftVoterState
{
    private readonly RaftVoterView _initial;
    private volatile RaftVoterView _current;

    public RaftVoterView Current => _current;

    public RaftVoterState(string[] initialIds)
    {
        _initial = _current = new RaftVoterView(initialIds, initialIds, false, 0);
    }

    public async Task ReadLog(ILogRepository log)
    {
        if (log.FirstAvailableIndex != 1)
        {
            throw new InvalidOperationException("Raft voter reconfiguration requires retaining the full log.");
        }

        var last = log.LastIndex.Index;
        var view = _initial;
        if (last > 0)
        {
            var entries = await log.GetLogsAtIndex(1, last);
            var configurationEntry = entries.LastOrDefault(entry => entry.VoterChange != null);
            if (configurationEntry?.VoterChange is { } config)
            {
                view = new RaftVoterView(config.OldVoterIds, config.NewVoterIds, config.IsJoint, configurationEntry.Index);
            }
        }
        _current = view;
    }

    public void Append(int index, RaftVoterChange change)
        => _current = new RaftVoterView(change.OldVoterIds, change.NewVoterIds, change.IsJoint, index);
}

internal sealed class RaftVoterView
{
    private readonly HashSet<string> _old;
    private readonly HashSet<string> _new;

    public string[] OldIds => _old.Order(StringComparer.Ordinal).ToArray();
    public string[] NewIds => _new.Order(StringComparer.Ordinal).ToArray();
    public bool IsJoint { get; }
    public int Index { get; }

    public RaftVoterView(string[] oldIds, string[] newIds, bool isJoint, int index)
    {
        _old = new(RaftVoterConfiguration.ValidateVoterIds(oldIds), StringComparer.Ordinal);
        _new = new(RaftVoterConfiguration.ValidateVoterIds(newIds), StringComparer.Ordinal);
        IsJoint = isJoint;
        Index = index;
    }

    public bool Contains(string id) => _new.Contains(id) || (IsJoint && _old.Contains(id));

    public bool HasQuorum(ISet<string> votes)
        => _new.Count(votes.Contains) > _new.Count / 2 && (!IsJoint || _old.Count(votes.Contains) > _old.Count / 2);

    public int QuorumIndex(Func<string, int> matchIndex)
    {
        var next = MajorityIndex(_new, matchIndex);
        return IsJoint ? Math.Min(next, MajorityIndex(_old, matchIndex)) : next;
    }

    public bool CanReplicateTo(string id, int committedIndex)
        => Contains(id) || (Index > committedIndex && _old.Contains(id));

    private static int MajorityIndex(HashSet<string> voters, Func<string, int> matchIndex)
        => voters.Select(matchIndex).OrderDescending().ElementAt(voters.Count / 2);
}
