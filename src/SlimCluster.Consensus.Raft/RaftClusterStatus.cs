namespace SlimCluster.Consensus.Raft;

using SlimCluster;

public class RaftClusterStatus : AbstractStatus, IClusterStatus
{
    protected RaftClusterStatus(Guid id, string name) : base(id, name)
    {
    }

    public static readonly RaftClusterStatus Initializing = new (new Guid("{8939D9A3-7B4F-4B6D-9248-F8086346C1F7}"), "Initializing");
    public static readonly RaftClusterStatus Working = new (new Guid("{87A7A4EE-1B22-4BAE-9021-FB56698BA6C8}"), "Working");
    public static readonly RaftClusterStatus Waiting = new (new Guid("{A5DED565-8580-4ED8-BB73-E1F102127322}"), "Waiting");
}

