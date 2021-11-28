using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Domain
{

    public interface ISession
    {
        void Connect();

        void Disconnect();

        SessionStatus Status { get; }

        ReadOnlyCollection<EventCategory> GetEventCategories();

        ReadOnlyCollection<Event> GetEvents(IEnumerable<EventCategory> categories);

        ReadOnlyCollection<Market> GetMarkets(IEnumerable<Event> events);

        IObservable<MarketObservation> GetMarketChangeStream(string marketId);

        ReadOnlyCollection<Order> GetOrders(string marketId);

        ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds);

        void PlaceOrders(IEnumerable<OrderRequest> orderRequests);

        void CancelOrders(IEnumerable<Order> orders);

        event EventHandler<SessionStatusChangeEventArgs> SessionStatusChanged;
    }
}