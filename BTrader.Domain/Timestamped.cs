using System;

namespace BTrader.Domain
{
    public class Timestamped
    {
        public Timestamped(DateTime timestamp)
        {
            Timestamp = timestamp;
        }

        public DateTime Timestamp { get; }
    }
}