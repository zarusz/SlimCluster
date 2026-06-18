namespace SlimCluster.Membership.Swim;

using Microsoft.Extensions.Options;

public class SwimClusterMembershipOptionsValidator : IValidateOptions<SwimClusterMembershipOptions>
{
    public ValidateOptionsResult Validate(string? name, SwimClusterMembershipOptions options)
    {
        var failures = new List<string>();

        if (options.ProtocolPeriod <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.ProtocolPeriod)} must be greater than zero.");
        }

        if (options.PingAckTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.PingAckTimeout)} must be greater than zero.");
        }

        if (options.ProtocolPeriod > TimeSpan.Zero && options.PingAckTimeout >= options.ProtocolPeriod)
        {
            failures.Add($"{nameof(options.PingAckTimeout)} must be less than {nameof(options.ProtocolPeriod)}.");
        }

        if (options.FailureDetectionSubgroupSize < 0)
        {
            failures.Add($"{nameof(options.FailureDetectionSubgroupSize)} must not be negative.");
        }

        if (options.MembershipEventBufferCount <= 0)
        {
            failures.Add($"{nameof(options.MembershipEventBufferCount)} must be greater than zero.");
        }

        if (options.MembershipEventPiggybackCount < 0)
        {
            failures.Add($"{nameof(options.MembershipEventPiggybackCount)} must not be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
