using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Domain
{
    public interface IMarketHistoryWriter<TEvent, TMarket, TMarketChange> : IObserver<TMarketChange>
    {
        void Write(IEnumerable<TEvent> eventTypes);
        void Write(string eventId, IEnumerable<TMarket> markets);
    }
}
