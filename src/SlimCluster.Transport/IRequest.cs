namespace SlimCluster.Transport;

public interface IRequest<TResponse> : IHasRequestId
{
}

public interface IResponse : IHasRequestId
{
}