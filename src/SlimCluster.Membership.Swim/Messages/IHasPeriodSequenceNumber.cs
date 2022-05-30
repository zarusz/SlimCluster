namespace SlimCluster.Membership.Swim.Messages;

using Newtonsoft.Json;

public interface IHasPeriodSequenceNumber
{
    /// <summary>
    /// Protocol Period Sequence Number of the Node that sent this Ping message
    /// </summary>
    [JsonProperty("psn")]
    long PeriodSequenceNumber { get; set; }
}
