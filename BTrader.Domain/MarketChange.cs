using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class MarketChange
    {

        public MarketChange(DateTime timestamp, MarketStatus status, ReadOnlyCollection<OutcomeChange> outcomeChanges)
        {
            Timestamp = timestamp;
            Status = status;
            Outcomes = outcomeChanges;
        }

        public DateTime Timestamp { get; }
        public MarketStatus Status { get; }

        public ReadOnlyCollection<OutcomeChange> Outcomes { get; }
    }
}