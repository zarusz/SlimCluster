﻿namespace SlimCluster.Membership.Swim.Messages
{
    using Newtonsoft.Json;

    public class PingReqMessage : PingMessage, IHasNodeAddress
    {
        /// <summary>
        /// Node address that sent this Ping
        /// </summary>
        public string NodeAddress { get; set; } = string.Empty;

        /// <summary>
        /// Node port that sent this Ping
        /// </summary>
        public int NodePort { get; set; }
    }
}
