namespace SlimCluster.Consensus.Raft;

public class RaftFollowerState
{
    private readonly RaftConsensusOptions _options;
    private readonly ITime _time;
    private readonly ILogger<RaftFollowerState> _logger;

    private readonly int _term;

    public DateTimeOffset LeaderTimeout { get; protected set; }

    public INode? Leader { get; protected set; }

    public RaftFollowerState(ILogger<RaftFollowerState> logger, RaftConsensusOptions options, ITime time, int term, INode? leaderNode)
    {
        _logger = logger;
        _options = options;
        _time = time;
        _term = term;
        Leader = leaderNode;
        OnLeaderMessage(leaderNode);
    }

    public void OnLeaderMessage(INode? leaderNode)
    {
        LeaderTimeout = _time.Now.Add(_options.LeaderTimeout);
        if (Leader != leaderNode)
        {
            Leader = leaderNode;
            _logger.LogInformation("New leader is {Node} for term {Term}", Leader, _term);
        }
    }
}