namespace SlimCluster.Persistence;

public interface IClusterPersistenceService
{
    Task Persist(CancellationToken cancellationToken);
    Task Restore(CancellationToken cancellationToken);
}

