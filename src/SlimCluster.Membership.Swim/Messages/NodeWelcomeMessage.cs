namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Message sent by other nodes as a response to the node joining the cluster.
    /// </summary>
    public class NodeWelcomeMessage : IHasNodeId
    {
        public string NodeId { get; set; } = string.Empty;

        /// <summary>
        /// The list of active nodes that are know to date.
        /// </summary>
        [JsonProperty("nodes")]
        public IList<ActiveNode> Nodes { get; set; } = new List<ActiveNode>();
    }
}
