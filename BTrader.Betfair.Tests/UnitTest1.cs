using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Api_ng_sample_code;
using Api_ng_sample_code.TO;
using BTrader.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BTrader.Betfair.Tests
{
    [TestClass]
    public class UnitTest1
    {
        

        [TestMethod]
        public void GetSoccerOutcomeOdds()
        {
            var sessionProvider = new AppKeySessionProvider(AppKeySessionProvider.SSO_HOST_COM, Betfair.AppKeySessionProvider.LIVEAPPKEY, "", "");
            var streamSession = new BetfairStreamSession(() => new BetfairConnection("stream-api.betfair.com", 443),
                                sessionProvider,
                                new DebugLogger());
            var client = new JsonRpcClient("https://api.betfair.com/exchange/betting", Betfair.AppKeySessionProvider.LIVEAPPKEY, sessionProvider.GetOrCreateSession());
            var categoryId = "1";
            var timeNow = DateTime.UtcNow;
            var hourIncrement = 24;
            var time = new TimeRange();
            time.From = timeNow;
            time.To = time.From + TimeSpan.FromHours(hourIncrement);

            var rootOutputPath = @"D:\Data\compositeodds";

            var marketFilter = new MarketFilter();
            marketFilter.EventTypeIds = new HashSet<string>(new[] { categoryId });
            marketFilter.MarketStartTime = time;
            ISet<MarketProjection> marketProjections = new HashSet<MarketProjection>();
            marketProjections.Add(MarketProjection.EVENT);
            marketProjections.Add(MarketProjection.RUNNER_DESCRIPTION);
            marketProjections.Add(MarketProjection.MARKET_DESCRIPTION);
            var marketSort = MarketSort.FIRST_TO_START;
            var maxResults = "200";

            ISet<PriceData> priceData = new HashSet<PriceData>();
            //get all prices from the exchange
            priceData.Add(PriceData.EX_BEST_OFFERS);
            priceData.Add(PriceData.EX_TRADED);

            var priceProjection = new PriceProjection();
            priceProjection.PriceData = priceData;

            var events = client.listEvents(marketFilter);//, marketProjections, marketSort, maxResults);
            foreach(var e in events)
            {
                var eventId = e.Event.Id;
                marketFilter = new MarketFilter();
                marketFilter.EventIds = new HashSet<string>(new[] { eventId });
                var markets = client.listMarketCatalogue(marketFilter, marketProjections, marketSort, maxResults);

                using (var writer = new StreamWriter(Path.Combine(rootOutputPath, $"{eventId}.csv")))
                {
                    foreach(var m in markets)
                    {
                        var marketBooks = client.listMarketBook(new[] { m.MarketId }.ToList(), priceProjection)
                            .SelectMany(o => o.Runners)
                            .ToDictionary(o => $"{o.SelectionId}:{o.Handicap}", o => o);
                        foreach (var r in m.Runners)
                        {
                            var selectionId = $"{r.SelectionId}:{r.Handicap}";
                            var runnerOdds = marketBooks[selectionId];
                            var bestToBack = runnerOdds.ExchangePrices.AvailableToBack.OrderByDescending(p => p.Price).FirstOrDefault();
                            var bestToLay = runnerOdds.ExchangePrices.AvailableToLay.OrderBy(p => p.Price).FirstOrDefault();
                            writer.WriteLine(string.Join(",", new string[]
                            {
                                m.MarketId,
                                m.Description.MarketType,
                                r.SelectionId.ToString(),
                                r.Handicap.ToString(),
                                r.RunnerName,
                                $"{bestToBack?.Size}",
                                $"{bestToBack?.Price}",
                                $"{bestToLay?.Price}",
                                $"{bestToLay?.Size}",
                            }));
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void MaxConnectionsTest()
        {
            var waitHandle = new ManualResetEventSlim();
            var sessionProvider = new AppKeySessionProvider(AppKeySessionProvider.SSO_HOST_COM, Betfair.AppKeySessionProvider.LIVEAPPKEY, "", "");
            using (var disposables = new CompositeDisposable())
            {
                for (var i = 0; i < 2; i++)
                {
                    var streamSession = new BetfairStreamSession(() => new BetfairConnection("stream-api-integration.betfair.com", 443),
                                        sessionProvider,
                                        new DebugLogger());
                    streamSession.Open();

                }

                waitHandle.Wait(10000);
            }
        }

        [TestMethod]
        public void HistoryReaderTest()
        {
            var basePath = @"D:\Data\btrader\betfair";
            var historyReader = new BetfairHistoryReaderWriter(basePath, false);
            var simulationSession = new SimulationSession(null, historyReader, null, null);
            simulationSession.SimulationDate = DateTime.Today.AddDays(1);
            var categories = simulationSession.GetEventCategories();
            var horseRacing = categories.Single(c => c.Name == "Horse Racing");
            var events = simulationSession.GetEvents(new[] { horseRacing });

        }

        [TestMethod]
        public void HistoryReaderStreamingTest()
        {
            var basePath = @"D:\Data\btrader\betfair";
            var historyReader = new BetfairHistoryReaderWriter(basePath, false);
            var simulationSession = new SimulationSession(null, historyReader, null, null);
            simulationSession.SimulationDate = new DateTime(2019, 10, 11);
            var categories = simulationSession.GetEventCategories();
            var horseRacing = categories.Single(c => c.Name == "Horse Racing");
            var events = simulationSession.GetEvents(new[] { horseRacing });
            //var selectedId = "1.163414874";
            var marketObservations = events.SelectMany(e => e.Markets.Values).ToArray();
            //var simulationStream = simulationSession.GetMarketChangeStream("1.163414915");

            var existingStreams = Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories).Where(f => f.Contains("marketstreams")).ToArray();
            var marketsWithStreams = new List<MarketObservation>();
            foreach(var m in marketObservations)
            {
                if(existingStreams.Any(s => s.Contains(m.Id)))
                {
                    marketsWithStreams.Add(m);
                }
            }

            var marketObservation = marketsWithStreams.First();

            var market = Market.FromObservation(marketObservation, simulationSession.GetMarketChangeStream(marketObservation.Id));
            var waitHandle = new ManualResetEventSlim();
            using(var sub = market.Changes.Subscribe(m =>
            {
                foreach(var outcome in market.Outcomes.Values)
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
