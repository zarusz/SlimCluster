namespace SlimCluster.Membership.Swim;

public class UnknownAddress : IAddress
{
    bool IEquatable<IAddress>.Equals(IAddress other) => other is UnknownAddress;

    IAddress IAddress.Parse(string value) => throw new NotImplementedException();

    protected UnknownAddress()
    {
    }

    public static readonly IAddress Instance = new UnknownAddress();
}
