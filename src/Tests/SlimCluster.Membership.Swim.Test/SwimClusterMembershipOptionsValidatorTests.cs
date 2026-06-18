namespace SlimCluster.Membership.Swim.Tests;

public class SwimClusterMembershipOptionsValidatorTests
{
    private readonly SwimClusterMembershipOptionsValidator _subject = new();

    [Fact]
    public void Given_DefaultOptions_When_Validate_Then_Succeeds()
    {
        var result = _subject.Validate(null, new SwimClusterMembershipOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Given_InvalidOptions_When_Validate_Then_Fails()
    {
        var result = _subject.Validate(null, new SwimClusterMembershipOptions
        {
            ProtocolPeriod = TimeSpan.FromSeconds(1),
            PingAckTimeout = TimeSpan.FromSeconds(1),
            FailureDetectionSubgroupSize = -1,
            MembershipEventBufferCount = 0,
            MembershipEventPiggybackCount = -1,
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(4);
    }
}
