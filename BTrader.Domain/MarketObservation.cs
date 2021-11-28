using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BTrader.Domain
{
    public class MarketObservation
    {

        public MarketObservation(string id, DateTime? start, DateTime timestamp, string name, MarketStatus? status, string type, decimal? volume, IEnumerable<OutcomeObservation> outcomes)
        {
            this.Id = id;
            Start = start;
            this.Name = name;
            this.Timestamp = timestamp;
            this.Status = status;
            this.Type = type;
            this.Volume = volume;
            this.Outcomes = new ReadOnlyCollection<OutcomeObservation>(outcomes.ToList());
        }

        public DateTime Timestamp { get; }
        public string Id { get; }
        public DateTime? Start { get; }
        public string Name { get; }

        public MarketStatus? Status { get; }

        public IReadOnlyCollection<OutcomeObservation> Outcomes { get; }
        public string Type { get; }
        public decimal? Volume { get; }
    }
}