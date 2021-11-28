using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Domain
{
    public abstract class SimulationSessionBase : ISimulationSession
    {
        protected readonly Subject<MarketObservation> orderObservations = new Subject<MarketObservation>();
        private readonly Func<string, Market> marketSelector;
        private readonly string baseDir;

        public DateTime SimulationDate { get; set; }
        public SessionStatus Status { get; }
        public Dictionary<string, Dictionary<string, MarketTradingState>> tradingStates = new Dictionary<string, Dictionary<string, MarketTradingState>>();

        public event EventHandler<SessionStatusChangeEventArgs> SessionStatusChanged;

        public SimulationSessionBase(Func<string, Market> marketSelector, string baseDir)
        {
            this.marketSelector = marketSelector;
            this.baseDir = baseDir;
        }

        public void CancelOrders(IEnumerable<Order> orders)
        {
            foreach (var order in orders)
            {
                this.GetOrCreateTradingState(order.MarketId, order.OutcomeId).CancelOrder(order);
            }
        }

        public abstract void Connect();
        public abstract void Disconnect();
        public abstract ReadOnlyCollection<EventCategory> GetEventCategories();
        public abstract ReadOnlyCollection<Event> GetEvents(IEnumerable<EventCategory> categories);
        public abstract IObservable<MarketObservation> GetMarketChangeStream(string marketId);

        public abstract ReadOnlyCollection<Market> GetMarkets(IEnumerable<Event> events);

        public ReadOnlyCollection<Order> GetOrders(string marketId)
        {
            var orders = this.GetTradingStates(marketId)
                .SelectMany(tradingState => tradingState.GetOrders())
                .ToList();
            return new ReadOnlyCollection<Order>(orders);
        }

        private MarketTradingState GetOrCreateTradingState(string marketId, string outcomeId)
        {
            lock (this.tradingStates)
            {
                var outcome = this.marketSelector(marketId).Outcomes[outcomeId];
                Dictionary<string, MarketTradingState> outcomeTradingStates;
                if(!this.tradingStates.TryGetValue(marketId, out outcomeTradingStates))
                {
                    outcomeTradingStates = new Dictionary<string, MarketTradingState>();
                }

                this.tradingStates[marketId] = outcomeTradingStates;
                MarketTradingState tradingState;
                if(!outcomeTradingStates.TryGetValue(outcomeId, out tradingState))
                {
                    var id = marketId + "-" + outcomeId;
                    tradingState = new MarketTradingState(this.baseDir, id, () => outcome.LastUpdate, outcome, this.orderObservations);
                    outcomeTradingStates[outcomeId] = tradingState;
                }

                return tradingState;
            }
        }

        private MarketTradingState[] GetTradingStates(string marketId)
        {
            lock (this.tradingStates)
            {
                return this.tradingStates[marketId].Values.ToArray();
            }
        }

        public void PlaceOrders(IEnumerable<OrderRequest> orderRequests)
        {
            foreach(var g in orderRequests.GroupBy(o => o.MarketId + "-" + o.OutcomeId))
            {
                var orders = g.ToArray();
                this.GetOrCreateTradingState(orders.First().MarketId, orders.First().OutcomeId).PlaceOrders(orders);
            }
        }

        public abstract ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds);

        public abstract bool HasStream(string marketId);
    }
}
