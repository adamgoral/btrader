using Api_ng_sample_code.TO;
using BFSwagger = Betfair.ESASwagger;
using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Betfair.ESASwagger.Model;
using System.IO;
using System.Reactive.Concurrency;

namespace BTrader.Betfair
{
    public class SimulationSession : SimulationSessionBase
    {
        private readonly IMarketHistoryReader<EventTypeResult, MarketCatalogue, MarketChangeMessage> historyReader;
        private readonly MarketMessageScheduler orderedStream;

        public SimulationSession(string baseDir, IMarketHistoryReader<EventTypeResult, MarketCatalogue, MarketChangeMessage> historyReader, Func<string, Market> marketLocator, MarketMessageScheduler orderedStream)
            : base(marketLocator, baseDir)
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
            var eventTypes = this.historyReader.GetEventTypes(this.SimulationDate);
            var result = eventTypes
                .Select(c => new EventCategory(c.EventType.Id, c.EventType.Name))
                .ToList();
            return new ReadOnlyCollection<EventCategory>(result);
        }

        public override ReadOnlyCollection<Domain.Event> GetEvents(IEnumerable<EventCategory> categories)
        {
            var categoryLookup = new Dictionary<string, EventCategory>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var category in categories)
            {
                categoryLookup[category.Id] = category;
            }

            var events = categoryLookup.Values
                .SelectMany(c => this.historyReader.GetEvents(this.SimulationDate, c.Id).Select(m => Tuple.Create(c, m))).ToArray();

            var list = new List<Domain.Event>();
            var marketBooks = new Dictionary<string, MarketBook>();
            foreach (var g in events.GroupBy(t => t.Item2.Event.Id))
            {
                var e = g.First().Item2.Event;
                var category = categoryLookup[g.First().Item1.Id];
                var ev = new Domain.Event(e.Id, e.Name, category, e.OpenDate.Value, g.Select(t => t.Item2).ToMarketObservation(marketBooks, this.SimulationDate));
                list.Add(ev);
            }

            return new ReadOnlyCollection<Domain.Event>(list);
        }

        public override IObservable<MarketObservation> GetMarketChangeStream(string marketId)
        {
            IObservable<MarketObservation> marketStream;

            if (this.orderedStream != null)
            {
                var messages = this.historyReader
                    .ReadMessages(marketId)
                    .Where(m => m.Mc != null)
                    .SelectMany(m => m.Mc.Select(i => Tuple.Create(m.ReceiveTime, i)))
                    .Select(tpl =>
                    {
                        var pt = tpl.Item1;
                        var m = tpl.Item2;
                        var timeStamp = new DateTime(1970, 1, 1).AddMilliseconds(pt.Value);
                        return m.ToMarketObservation(timeStamp);
                    });

                this.orderedStream.Queue(messages.Select(m => new TimestampedMessage<MarketObservation>(m.Timestamp, m)));

                marketStream = this.orderedStream.GetStream()
                    .Select(m => m as TimestampedMessage<MarketObservation>)
                    .Where(m => m != null)
                    .Select(m => m.Message)
                    .Where(m => m.Id == marketId);
            }
            else
            {
                marketStream = this.historyReader.GetMarketChangeStream(marketId)
                    .Where(m => m.Mc != null)
                    .SelectMany(m => m.Mc.Select(i => Tuple.Create(m.Pt, i)))
                    .Select(tpl =>
                    {
                        var pt = tpl.Item1;
                        var m = tpl.Item2;
                        var timeStamp = new DateTime(1970, 1, 1).AddMilliseconds(pt.Value);
                        return m.ToMarketObservation(timeStamp);
                    });
            }
            var oStream = this.orderObservations;
            return Observable.Merge(marketStream, oStream.Where(o => o.Id == marketId));
        }

        public override ReadOnlyCollection<Market> GetMarkets(IEnumerable<Domain.Event> events)
        {
            throw new NotImplementedException();
        }

        public override bool HasStream(string marketId)
        {
            return this.historyReader.HasStream(marketId);
        }

        public override ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds)
        {
            throw new NotImplementedException();
        }
    }
}
