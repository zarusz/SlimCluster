namespace SlimCluster.Membership
{
    using System;

    public class MemberEventArgs : EventArgs
    {
        public INode Node { get; }

        public DateTime Timestamp { get; }
        
        public MemberEventArgs(INode node, DateTime timestamp)
        {
            Node = node;
            Timestamp = timestamp;
        }
    }
}
