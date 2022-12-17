namespace SlimCluster;

public class ClusterOptions
{
    /// <summary>
    /// The logical cluster ID that identifies this cluster.
    /// </summary>
    public string ClusterId { get; set; } = "MyClusterId";

    /// <summary>
    /// The unique ID representing this node instance.
    /// </summary>
    public string NodeId { get; set; } = Guid.NewGuid().ToString();
}
