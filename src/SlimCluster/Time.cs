using System;

namespace SlimCluster
{
    public class Time : ITime
    {
        public DateTimeOffset Now => DateTimeOffset.UtcNow;
    }
}
