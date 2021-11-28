namespace BTrader.Matchbook
{
    public class MatchbookMarketId
    {

        public MatchbookMarketId(string marketKey)
        {
            var parts = marketKey.Split('-');
            this.EventId = long.Parse(parts[0]);
            this.MarketId = long.Parse(parts[1]);
        }

        public MatchbookMarketId(long eventId, long marketId)
        {
            this.EventId = eventId;
            this.MarketId = marketId;
        }

        public long EventId { get; }
        public long MarketId { get; }

        public override string ToString()
        {
            return $"{EventId}-{MarketId}";
        }

        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var other = obj as MatchbookMarketId;
            if (other == null) return false;
            return (this.EventId == other.EventId && this.MarketId == other.MarketId);
        }
    }
}