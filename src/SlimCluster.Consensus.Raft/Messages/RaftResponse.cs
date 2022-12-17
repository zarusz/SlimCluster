namespace SlimCluster.Consensus.Raft;

public abstract class RaftResponse : RaftMessage, IResponse
{
    protected RaftResponse()
    {
    }

    public RaftResponse(RaftMessage request) => RequestId = request.RequestId;
}
