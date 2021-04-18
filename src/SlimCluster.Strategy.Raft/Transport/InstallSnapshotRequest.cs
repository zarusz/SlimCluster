namespace SlimCluster.Strategy.Raft
{
    public class InstallSnapshotRequest
    {
        public int Term { get; set; }
        public string? LeaderId { get; set; }
        public int LastIncludedIndex { get; set; }
        public int LastIncludedTerm { get; set; }
        public int Offset { get; set; }
        public byte[]? Data { get; set; }
        public bool Done { get; set; }
    }
}
