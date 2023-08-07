namespace SlimCluster.Membership.Swim;

public class UnknownAddress : IAddress
{
    public static readonly IAddress Instance = new UnknownAddress();

    protected UnknownAddress()
    {
    }

    bool IEquatable<IAddress>.Equals(IAddress other) => other is UnknownAddress;

    IAddress IAddress.Parse(string value) => throw new NotImplementedException();

    public override string ToString() => "Unknown";
}
