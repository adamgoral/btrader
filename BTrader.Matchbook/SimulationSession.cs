using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Matchbook
{
    public class SimulationSession : SimulationSessionBase
    {
        private readonly IMarketHistoryReader<Sport, MatchbookEvent, MatchbookOrderBook> historyReader;
        private readonly MarketMessageScheduler orderedStream;

        public SimulationSession(string basePath, IMarketHistoryReader<Sport, MatchbookEvent, MatchbookOrderBook> historyReader, Func<string, Market> marketLocator, MarketMessageScheduler orderedStream) 
            : base(marketLocator, basePath)
        {
            this.historyReader = historyReader;
            this.orderedStream = orderedStream;
        }

        public override void Connect()
        {
            //
        }

        public override void Disconnect()
        {
            //
        }

        public override ReadOnlyCollection<EventCategory> GetEventCategories()
        {
            var categories = this.historyReader.GetEventTypes(this.SimulationDate).Select(s => s.ToEventCategory()).ToList();
            return new ReadOnlyCollection<EventCategory>(categories);
        }

        public override ReadOnlyCollection<Event> GetEvents(IEnumerable<EventCategory> categories)
        {
            var categoryLookup = new Dictionary<string, EventCategory>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var category in categories)
            {
                categoryLookup[category.Id] = category;
            }

            var events = categoryLookup.Values.SelectMany(c => this.historyReader.GetEvents(this.SimulationDate, c.Id)).Select(e => e).ToList();
            var list = new List<Event>();
            foreach (var e in events)
            {
                var participants = new Dictionary<long, EventParticipant>();
                if (e.Participants != null)
                {
                    participants = e.Participants.ToDictionary(p => p.Id, p => p);
                }
                var domainMarkets = new List<MarketObservation>();
                foreach (var market in e.Markets)
                {
                    var domainMarket = market.ToObservation(participants);
                    domainMarkets.Add(domainMarket);
                }

                list.Add(new Event(e.Id.ToString(), e.Name, categoryLookup[e.SportId.ToString()], e.Start, domainMarkets));
            }

            return new ReadOnlyCollection<Event>(list);
        }

        public override IObservable<MarketObservation> GetMarketChangeStream(string marketId)
        {
            IObservable<MarketObservation> marketStream;
            if (this.orderedStream != null)
            {
                var messages = this.historyReader.ReadMessages(marketId)
                    .Select(m => new TimestampedMessage<MarketObservation>(m.Timestamp, m.ToObservation()));
                this.orderedStream.Queue(messages);
                marketStream = this.orderedStream.GetStream()
                    .Select(m => m as TimestampedMessage<MarketObservation>)
                    .Where(m => m != null)
                    .Select(m => m.Message)
                    .Where(m => m.Id == marketId);

            }
            else
            {
                marketStream = this.historyReader.GetMarketChangeStream(marketId).Select(m => m.ToObservation());
            }
            var oStream = this.orderObservations;
            return Observable.Merge(marketStream, oStream.Where(o => o.Id == marketId));
        }

        public override ReadOnlyCollection<Market> GetMarkets(IEnumerable<Event> events)
        {
            throw new NotImplementedException();
        }

        public override ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds)
        {
            throw new NotImplementedException();
        }

        public override bool HasStream(string marketId)
        {
            return this.historyReader.HasStream(marketId);
        }
    }
}
