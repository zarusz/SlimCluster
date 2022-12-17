namespace SlimCluster;

public interface IAddress : IEquatable<IAddress>
{
    IAddress Parse(string value);
}