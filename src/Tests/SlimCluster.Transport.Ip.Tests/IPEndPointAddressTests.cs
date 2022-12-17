namespace SlimCluster.Transport.Ip.Tests;

using System.Net;

public class IPEndPointAddressTests
{
    [Fact]
    public void Given_TwoSimilarAddress_When_Equals_Then_ReturnsTrue()
    {
        // arrange
        var a = new IPEndPointAddress(new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6001));
        var b = new IPEndPointAddress(new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6001));

        // act
        var equals = a.Equals(b);

        // asset
        equals.Should().BeTrue();
    }
}