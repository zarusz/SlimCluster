using System;

namespace SlimCluster
{
    public interface IStatus
    {
        Guid Id { get; }
        string Name { get; }
    }

    public interface IAddress
    {
    }
}
