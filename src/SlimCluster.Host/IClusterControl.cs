namespace SlimCluster.Host;

public interface IClusterControl
{
    /// <summary>
    /// Starts the cluster protocols (membership, consensus) and message exchange
    /// </summary>
    /// <returns></returns>
    Task Start();

    /// <summary>
    /// Stops the cluster protocols (membership, consensus) and message exchange
    /// </summary>
    /// <returns></returns>
    Task Stop();
}