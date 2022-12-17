namespace SlimCluster;

using System.Runtime.Serialization;

public class ClusterException : Exception
{
    public ClusterException()
    {
    }

    public ClusterException(string message) : base(message)
    {
    }

    public ClusterException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected ClusterException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}