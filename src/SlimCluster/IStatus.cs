namespace SlimCluster
{
    using System;

    public interface IStatus
    {
        Guid Id { get; }
        string Name { get; }
    }
}
