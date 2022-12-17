namespace SlimCluster.Host.Common;

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

    public void Mutate(Action<List<T>> action) => Mutate<object?>(list => { action(list); return null; });

    public TResult Mutate<TResult>(Func<List<T>, TResult> action) where TResult : class?
    {
        TResult result;
        lock (_listLock)
        {
            result = action(_list);
            _readOnlyList = _list.AsReadOnly();
        }

        Changed?.Invoke(this);
        return result;
    }
}
