using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class Event
    {
        public Event(string id, string name, EventCategory eventCategory, DateTime startDateTime, IEnumerable<MarketObservation> markets)
        {
            this.Id = id;
            this.Name = name;
            this.Category = eventCategory;
            this.StartDateTime = startDateTime;
            this.Markets = markets.ToDictionary(m => m.Id, m => m, StringComparer.CurrentCultureIgnoreCase);
        }

        public string Id { get; }

        public string Name { get; }

        public EventCategory Category { get; }

        public DateTime StartDateTime { get; }

        public IReadOnlyDictionary<string, MarketObservation> Markets { get; }
    }
}