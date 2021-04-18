namespace SlimCluster.Strategy.Raft
{
    public class LogEntry
    {
        public int Index { get; set; }
        public int Term { get; set; }
        public object Value { get; set; }

        public LogEntry(int index, int term, object value)
        {
            Index = index;
            Term = term;
            Value = value;
        }
    }

}
