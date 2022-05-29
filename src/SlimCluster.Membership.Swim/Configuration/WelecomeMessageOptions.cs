namespace SlimCluster.Membership.Swim
{
    public class WelecomeMessageOptions
    {
        /// <summary>
        /// Is Welcome message sent to the newly joined member from this node?
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        /// <summary>
        /// Is the welcome message randomy sent (by tossing a coin from this member)?
        /// </summary>
        public bool IsRandom { get; set; } = true;
    }
}
