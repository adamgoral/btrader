using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Domain
{
    public interface IMarketHistoryReader<TEvent, TMarket, TMarketChange>
    {
        IList<TMarket> GetEvents(DateTime date, string eventId);

        IList<TEvent> GetEventTypes(DateTime date);

        IObservable<TMarketChange> GetMarketChangeStream(string marketId);
        IEnumerable<TMarketChange> ReadMessages(string marketId);
        bool HasStream(string marketId);
    }
}
