namespace SlimCluster;

public abstract class AbstractStatus<T> : IStatus
    where T: IStatus
{
    public Guid Id { get; }

    public string Name { get; }

    protected AbstractStatus(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    #region equals

    public override bool Equals(object? obj)
        => obj is AbstractStatus<T> state && Id.Equals(state.Id);

    public override int GetHashCode()
        => HashCode.Combine(Id);

    #endregion

    public override string ToString()
        => Name;

    public static IReadOnlyCollection<T> All
    {
        get => typeof(T)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(x => typeof(T).IsAssignableFrom(x.FieldType))
            .Select(x => (T)x.GetValue(null))
            .ToList();
    }

    public static T FromId(Guid id) => All.FirstOrDefault(x => x.Id == id);
}
