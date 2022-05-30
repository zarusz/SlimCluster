namespace SlimCluster;

/// <summary>
/// The <see cref="INode"/> that represents the currently running process.
/// </summary>
public interface ICurrentNode : INode
{
    ICluster Cluster { get; }
}
