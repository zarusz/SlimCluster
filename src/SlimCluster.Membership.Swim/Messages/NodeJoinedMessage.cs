namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public class NodeJoinedMessage : NodeMessage, IHasNodeId
    {
        public string NodeId { get; set; } = string.Empty;

        [JsonProperty("inc")]
        public int Incarnation { get; set; }

        public NodeJoinedMessage(string nodeId, int incarnation)
        {
            NodeId = nodeId;
            Incarnation = incarnation;
        }
    }
}
