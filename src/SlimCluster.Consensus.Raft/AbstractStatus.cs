namespace SlimCluster.Consensus.Raft;

using SlimCluster;

public abstract class AbstractStatus : IStatus
{
    public Guid Id { get; }

    public string Name { get; }

    protected AbstractStatus(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    #region equals

    public override bool Equals(object? obj)
        => obj is AbstractStatus state && Id.Equals(state.Id);

    public override int GetHashCode()
        => HashCode.Combine(Id);

    #endregion

    public override string ToString()
        => Name;
}
