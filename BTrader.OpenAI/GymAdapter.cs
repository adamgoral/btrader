using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.OpenAI
{
    public class GymAdapter
    {
        private Market market;
        private readonly Outcome outcome;
        private MarketMessageScheduler scheduler;

        public GymAdapter(Market market, Outcome outcome, MarketMessageScheduler scheduler)
        {
            this.market = market;
            this.outcome = outcome;
            this.scheduler = scheduler;
        }



        private static ISimulationSession GetBetfairSimulationSession(string basePath, string reportBasePath, MarketMessageScheduler scheduler, Func<string, Market> marketLocator)
        {
            var historyReader = new Betfair.BetfairHistoryReaderWriter($@"{basePath}\betfair", false);
            var simulationSession = new Betfair.SimulationSession(reportBasePath, historyReader, marketLocator, scheduler);
            return simulationSession;
        }

        private static string GetBasePath()
        {
            var choices = new[]
            {
                @"c:\users\adam\data\btrader",
                @"D:\Data\btrader"
            };

            foreach (var choice in choices)
            {
                if (Directory.Exists(choice)) return choice;
            }

            throw new ApplicationException("Could not identify base path");
        }

        public static IEnumerable<MarketObservation> GetMarkets(DateTime date)
        {
            var markets = new Dictionary<string, Market>();
            var basePath = GetBasePath();
            var reportBasePath = $@"{basePath}\reports\hfttests_v2";
            var scheduler = new MarketMessageScheduler();

            var session = GetBetfairSimulationSession(basePath, reportBasePath, scheduler, id => markets[id]);
            session.SimulationDate = date;
            var selectedCategory = session.GetEventCategories().Single(c => c.Name == "Horse Racing");
            return session
                .GetEvents(new[] { selectedCategory })
                .SelectMany(ev => ev.Markets.Values)
                .Where(m => m.Type == "WIN");
        }

        public static GymAdapter Create(MarketObservation marketObservation, string outcomeId)
        {
            var log = new DebugLogger();
            var scheduler = new MarketMessageScheduler();
            var sessions = new Dictionary<string, ISimulationSession>();
            var markets = new Dictionary<string, Market>();
            var basePath = GetBasePath();
            var reportBasePath = $@"{basePath}\reports\hfttests_v2";
            var session = GetBetfairSimulationSession(basePath, reportBasePath, scheduler, id => markets[id]);
            session.SimulationDate = marketObservation.Start.Value.Date;

            var market = Market.FromObservation(marketObservation, session.GetMarketChangeStream(marketObservation.Id));
            var outcome = market.Outcomes.Values.FirstOrDefault(o => o.Id == outcomeId);
            if(outcome == null)
            {
                throw new ApplicationException($"Could not locate outcome id {outcomeId}");
            }

            var result = new GymAdapter(market, outcome, scheduler);
            return result;
        }
    }
}
