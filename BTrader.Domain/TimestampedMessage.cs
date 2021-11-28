using System;

namespace BTrader.Domain
{
    public class TimestampedMessage<T> : Timestamped
    {
        public TimestampedMessage(DateTime timestamp, T message) : base(timestamp)
        {
            this.Message = message;
        }

        public T Message { get; }

        public override string ToString()
        {
            return $"{this.Timestamp} {this.Message}";
        }
    }
}