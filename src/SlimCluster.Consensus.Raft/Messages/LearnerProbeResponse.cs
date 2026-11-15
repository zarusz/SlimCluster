namespace SlimCluster.Consensus.Raft;

internal sealed class LearnerProbeResponse : RaftResponse
{
    public bool IsStandbyLearner { get; set; }

    private LearnerProbeResponse()
    {
    }

    public LearnerProbeResponse(LearnerProbeRequest request) : base(request)
    {
    }
}
