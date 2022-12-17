namespace SlimCluster.Consensus.Raft;
public class RequestVoteRequest : RaftMessage, IHasTerm
{
    /// <summary>
    /// Candidate's term.
    /// </summary>
    public int Term { get; set; }
    /// <summary>
    /// Candidate requesting vote.
    /// </summary>
    public string CandidateId { get; set; } = string.Empty;
    /// <summary>
    /// Candidate's last log index.
    /// </summary>
    public int LastLogIndex { get; set; }
    /// <summary>
    /// Candidate's last log term.
    /// </summary>
    public int LastLogTerm { get; set; }

    public override string ToString() => $"{GetType().Name}(Term={Term},CandidateId={CandidateId},LastLogIndex={LastLogIndex},LastLogTerm={LastLogTerm})";
}
