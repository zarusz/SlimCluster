namespace SlimCluster.Consensus.Raft;

using Newtonsoft.Json;

public interface IHasTerm
{
    [JsonProperty("t")]
    public int Term { get; set; }
}
