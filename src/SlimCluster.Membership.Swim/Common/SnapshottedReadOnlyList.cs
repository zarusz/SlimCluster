namespace SlimCluster.Membership.Swim;

using System.Collections;

public class SnapshottedReadOnlyList<T> : IReadOnlyList<T>
{
    private readonly object _listLock = new();
    private readonly List<T> _list = new();
    private IReadOnlyList<T> _readOnlyList;

    public event Action<SnapshottedReadOnlyList<T>>? Changed;

    public IEnumerator<T> GetEnumerator() => _readOnlyList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _readOnlyList.GetEnumerator();
    public int Count => _readOnlyList.Count;
    public T this[int index] => _readOnlyList[index];

    public SnapshottedReadOnlyList() => _readOnlyList = _list.AsReadOnly();

    public void Mutate(Action<List<T>> action)
    {
        lock (_listLock)
        {
            action(_list);
            _readOnlyList = _list.AsReadOnly();
        }

        Changed?.Invoke(this);
    }
}
