namespace SlimCluster.Membership.Swim
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    public class SnapshottedReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly object listLock = new();
        private readonly List<T> list = new();
        private IReadOnlyList<T> readOnlyList;

        public IEnumerator<T> GetEnumerator() => readOnlyList.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => readOnlyList.GetEnumerator();
        public int Count => readOnlyList.Count;
        public T this[int index] => readOnlyList[index];

        public SnapshottedReadOnlyList() => readOnlyList = list.AsReadOnly();

        public void Mutate(Action<List<T>> action)
        {
            lock (listLock)
            {
                action(list);
                readOnlyList = list.AsReadOnly();
            }
        }
    }
}
