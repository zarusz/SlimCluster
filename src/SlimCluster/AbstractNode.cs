namespace SlimCluster;

public abstract class AbstractNode : INode, IEquatable<AbstractNode?>
{
    public string Id { get; }
    public abstract IAddress Address { get; protected set; }
    public abstract INodeStatus Status { get; }

    public AbstractNode(string id) => Id = id;

    public override string ToString() => $"{Id}@{Address}";

    #region equals
    
    public override bool Equals(object? obj) => Equals(obj as AbstractNode);
    public bool Equals(AbstractNode? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => HashCode.Combine(Id);
    
    #endregion
}