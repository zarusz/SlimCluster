namespace SlimCluster.Strategy.Raft
{
    public class Snapshot
    {
        public int Index { get; set; }
        public int Term { get; set; }
        public object State { get; set; }

        public Snapshot(int index, int term, object state)
        {
            Index = index;
            Term = term;
            State = state;
        }
    }
}

