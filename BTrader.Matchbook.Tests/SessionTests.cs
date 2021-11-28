using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using BTrader.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BTrader.Matchbook.Tests
{
    [TestClass]
    public class SessionTests
    {
        public MatchbookRestClient CreateRestClient()
        {
            return new MatchbookRestClient(new JsonRestClient("api-doc-test-client"), TimeSpan.FromMilliseconds(10), new DebugLogger());
        }

        public Session CreateSession()
        {
            var historyWriter = new MatchbookHistoryReaderWriter("matchbook", false);
            return new Session(this.CreateRestClient(), TimeSpan.FromSeconds(3), historyWriter);
        }

        public Session CreateSession(IMarketHistoryWriter<Sport, MatchbookEvent, MatchbookOrderBook> historyWriter)
        {
            return new Session(this.CreateRestClient(), TimeSpan.FromSeconds(3), historyWriter);
        }

        [TestMethod]
        public void SubscriptionTest()
        {
            using (var session = this.CreateSession())
            {
                session.Connect();
                var categories = session.GetEventCategories();
                var horseRacing = categories.First(c => c.Name == "Horse Racing");
                var events = session.GetEvents(new[] { horseRacing });
                var closestEvent = events.OrderBy(e => e.StartDateTime).First();
                var waitHandle = new ManualResetEventSlim(false);
                using (var disposables = new CompositeDisposable())
                {
                    foreach (var market in closestEvent.Markets)
                    {
                        var marketId = market.Key;
                        var m = Market.FromObservation(market.Value, session.GetMarketChangeStream(marketId));
                        disposables
                            .Add(m.Changes.Subscribe(change =>
                        {
                            Debug.WriteLine($"{change.Timestamp} {marketId} {change.Status}\n" + string.Join(Environment.NewLine, change.Outcomes.Select(o => $"{o.Id}: " + string.Join(",", o.OrderBook.ToLay))));
                        }));
                    }

                    waitHandle.Wait(TimeSpan.FromSeconds(20));
                }

                session.Disconnect();
            }
        }

        [TestMethod]
        public void FixStoredEvents()
        {
            var basePath = @"D:\Data\btrader\matchbook";
            var historyReader = new MatchbookHistoryReaderWriter(basePath, false);
            var simulationSession = new SimulationSession(null, historyReader, null, null);
            simulationSession.SimulationDate = new DateTime(2019, 10, 11);
            var categories = simulationSession.GetEventCategories();
            var horseRacing = categories.Single(c => c.Name == "Horse Racing");
            var events = historyReader.GetEvents(simulationSession.SimulationDate, horseRacing.Id);
            historyReader.Write(horseRacing.Id, events);
            //var selectedId = "1.163414874";
            var waitHandle = new ManualResetEventSlim();
            waitHandle.Wait(100000);
        }

        [TestMethod]
        public void HistoryReaderStreamingTest()
        {
            var basePath = @"C:\Users\adam\Data\btrader\matchbook";
            var historyReader = new MatchbookHistoryReaderWriter(basePath, false);
            var simulationSession = new SimulationSession(null, historyReader, null, null);
            simulationSession.SimulationDate = new DateTime(2019, 10, 10);
            var categories = simulationSession.GetEventCategories();
            var horseRacing = categories.Single(c => c.Name == "Horse Racing");
            var events = simulationSession.GetEvents(new[] { horseRacing });
            //var selectedId = "1.163414874";
            var marketObservations = events.SelectMany(e => e.Markets.Values).ToArray();
            var existingStreams = Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories).Where(f => f.Contains("marketstreams")).ToArray();
            var marketsWithStreams = new List<MarketObservation>();
            foreach (var m in marketObservations)
            {
                if (existingStreams.Any(s => s.Contains(m.Id)))
                {
                    marketsWithStreams.Add(m);
                }
            }

            var marketObservation = marketsWithStreams.Skip(10).First();
            //var simulationStream = simulationSession.GetMarketChangeStream("1.163414915");
            var market = Market.FromObservation(marketObservation, simulationSession.GetMarketChangeStream(marketObservation.Id));
            var waitHandle = new ManualResetEventSlim();
            using (var sub = market.Changes.Subscribe(m =>
            {
                foreach (var outcome in market.Outcomes.Values)
                {
                    var orderBook = outcome.OrderBook.Clone();
                    if (orderBook.ToBack.Keys.Any())
                    {
                        Debug.WriteLine($"{m.Timestamp} {m.Status} {outcome.Status} {orderBook.ToBack.Keys.Max()}");
                    }
                }

            }))
            {
                waitHandle.Wait(120000);
            }
        }
    }
}
