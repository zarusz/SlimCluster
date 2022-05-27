namespace SlimCluster.Membership.Swim
{
    using SlimCluster.Membership.Swim.Messages;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents a buffer of member (node) updates (joins, leaves, faults)
    /// </summary>
    public class SwimMembershipEventBuffer
    {
        internal class BufferItem
        {
            public MembershipEvent MemberEvent { get; private set; }
            public int UsedCount { get; private set; }

            public BufferItem(MembershipEvent memberEvent)
            {
                MemberEvent = memberEvent;
                UsedCount = 0;
            }

            public void Increment() => UsedCount++;
        }

        private readonly List<BufferItem> items;
        private readonly object itemsLock = new();

        public SwimMembershipEventBuffer(int bufferSize)
        {
            items = new List<BufferItem>(bufferSize);
        }

        /// <summary>
        /// Adds the event to the buffer if not exists already
        /// </summary>
        /// <param name="e"></param>
        /// <returns>ture when the event did not exist before, false of it did exist already</returns>
        public bool Add(MembershipEvent e)
        {
            lock (itemsLock)
            {
                if (items.Any(x => x.MemberEvent.Equals(e)))
                {
                    // if contains the event already then skip
                    return false;
                }

                var newItem = new BufferItem(e);

                var indexOfNodeEvent = items.FindIndex(x => x.MemberEvent.NodeId == e.NodeId);
                if (indexOfNodeEvent != -1)
                {
                    // Replace the previous event for the same node (prefer younger event)
                    if (items[indexOfNodeEvent].MemberEvent.Timestamp < newItem.MemberEvent.Timestamp)
                    {
                        items[indexOfNodeEvent] = newItem;
                        return true;
                    }
                    return false;
                }

                if (items.Count + 1 < items.Capacity)
                {
                    items.Add(newItem);
                }
                else
                {
                    // replace the element that was piggybacked the most times

                    var maxIndex = -1;
                    var maxItem = null as BufferItem;
                    for (var i = 0; i < items.Count; i++)
                    {
                        var item = items[i];

                        if (maxItem == null
                            || item.UsedCount > maxItem.UsedCount
                            || (item.UsedCount == maxItem.UsedCount && item.MemberEvent.Timestamp < maxItem.MemberEvent.Timestamp))
                        {
                            maxItem = items[i];
                            maxIndex = i;
                        }
                    }

                    items[maxIndex] = newItem;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets the least used events (by count)
        /// </summary>
        /// <param name="top"></param>
        /// <returns></returns>
        public IReadOnlyCollection<MembershipEvent> GetNextEvents(int top)
        {
            lock (itemsLock)
            {
                var selectedItems = items
                    .OrderBy(x => x.UsedCount)
                    .ThenBy(x => x.MemberEvent.Timestamp)
                    .Take(top)
                    .ToList();

                // Increment how many times was announced
                selectedItems.ForEach(x => x.Increment());

                return selectedItems.Select(x => x.MemberEvent).ToList();
            }
        }
    }
}
