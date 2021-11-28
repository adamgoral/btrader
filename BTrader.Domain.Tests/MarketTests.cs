using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BTrader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BTrader.Domain.Tests
{
    [TestClass]
    public class MarketTests
    {

        private Tuple<Betfair.SimulationSession, MarketObservation[]> GetBetfairMarkets(MarketMessageScheduler streamSorter, DateTime date)
        {
            var basePath = @"D:\Data\btrader\betfair";
            var historyReader = new Betfair.BetfairHistoryReaderWriter(basePath, false);
            var simulationSession = new Betfair.SimulationSession(null, historyReader, null, streamSorter);
            simulationSession.SimulationDate = date;
            var categories = simulationSession.GetEventCategories();
            var horseRacing = categories.Single(c => c.Name == "Horse Racing");
            var events = simulationSession.GetEvents(new[] { horseRacing });
            //var selectedId = "1.163414874";
            var marketObservations = events.SelectMany(e => e.Markets.Values).ToArray();
            //var simulationStream = simulationSession.GetMarketChangeStream("1.163414915");

            var existingStreams = Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories).Where(f => f.Contains("marketstreams")).ToArray();
            var marketsWithStreams = new List<MarketObservation>();
            foreach (var m in marketObservations)
            {
                if (existingStreams.Any(s => s.Contains(m.Id)))
                {
                    marketsWithStreams.Add(m);
                }
            }

            return Tuple.Create(simulationSession, marketsWithStreams.ToArray());
        }

        public Tuple<Matchbook.SimulationSession, MarketObservation[]> GetMatchbookMarkets(MarketMessageScheduler streamSorter, DateTime date)
        {
            var basePath = @"D:\Data\btrader\matchbook";
            var historyReader = new Matchbook.MatchbookHistoryReaderWriter(basePath, false);
            var simulationSession = new Matchbook.SimulationSession(null, historyReader, null, streamSorter);
            simulationSession.SimulationDate = date;
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

            return Tuple.Create(simulationSession, marketsWithStreams.ToArray());
        }

        private MarketObservation[] GetMatchingMarkets(DateTime date)
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        public void SynchronisedStreamsTest()
        {
            var date = new DateTime(2019, 10, 10);
            var streamSorter = new MarketMessageScheduler();
            var waitHandle = new ManualResetEventSlim();

            var betfairMarkets = GetBetfairMarkets(streamSorter, date);
            var startTimes = new HashSet<DateTime>(betfairMarkets.Item2.Where(m => m.Start != null).Select(m => m.Start.Value));
            var matchbookMarkets = GetMatchbookMarkets(streamSorter, date);
            var matchbookMarket = matchbookMarkets.Item2.First(m => startTimes.Contains(m.Start.Value));
            var betfairMarket = betfairMarkets.Item2.First(m => m.Start == matchbookMarket.Start);

            var bmarket = Market.FromObservation(betfairMarket, betfairMarkets.Item1.GetMarketChangeStream(betfairMarket.Id));
            var mmarket = Market.FromObservation(matchbookMarket, matchbookMarkets.Item1.GetMarketChangeStream(matchbookMarket.Id));
            using (var bsub = bmarket.Changes.Subscribe(bm =>
            {
                Debug.WriteLine($"betfair {bm.Timestamp}");
            }))
            {
                using(var msub = mmarket.Changes.Subscribe(mm => 
                {
                    Debug.WriteLine($"matchbook {mm.Timestamp}");
                }))
                {
                    var cancellationSource = new CancellationTokenSource();
                    streamSorter.Run(cancellationSource.Token);
                    waitHandle.Wait(100000);
                    cancellationSource.Cancel();
                }
            }
        }
    }
}
