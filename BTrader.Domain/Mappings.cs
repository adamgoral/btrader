using System;
using System.Collections.Generic;
using System.Linq;

namespace BTrader.Domain
{
    public static class Mappings
    {
        public static MarketObservation ToMarketObservation(this IEnumerable<Domain.Order> orders, string marketId, DateTime timeStamp, bool fullSnap)
        {
            var outcomeObservations = new List<OutcomeObservation>();
            foreach (var order in orders.Where(m => m.MarketId == marketId).GroupBy(o => o.OutcomeId))
            {
                var outcomeObservation = new OutcomeObservation(order.Key, timeStamp, null, null, null, order, null, fullSnap);
                outcomeObservations.Add(outcomeObservation);
            }

            return new MarketObservation(marketId, null, timeStamp, null, null, null, null, outcomeObservations);
        }
    }
}