namespace SlimCluster.Consensus.Raft;

using System.Runtime.Serialization;

public class RaftClusterException : ClusterException
{
    public RaftClusterException()
    {
    }

    public RaftClusterException(string message) : base(message)
    {
    }

    public RaftClusterException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected RaftClusterException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}