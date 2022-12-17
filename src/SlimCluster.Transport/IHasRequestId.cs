namespace SlimCluster.Transport;

public interface IHasRequestId
{
    public Guid RequestId { get; }
}
