using System;
using System.Linq;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using BTrader.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.IO;
using System.Globalization;
using BTrader.Python;

namespace BTrader.Algo.Tests
{
    [TestClass]
    public class AgentTests
    {
        private ISimulationSession GetBetfairSimulationSession(string basePath, string reportBasePath, MarketMessageScheduler scheduler, Func<string, Market> marketLocator)
        {
            var historyReader = new Betfair.BetfairHistoryReaderWriter($@"{basePath}\betfair", false);
            var simulationSession = new Betfair.SimulationSession(reportBasePath, historyReader, marketLocator, scheduler);
            return simulationSession;
        }

        private ISimulationSession GetMatchbookSimulationSession(string basePath, string reportBasePath, MarketMessageScheduler scheduler, Func<string, Market> marketLocator)
        {
            var historyReader = new Matchbook.MatchbookHistoryReaderWriter($@"{basePath}\matchbook", false);
            var simulationSession = new Matchbook.SimulationSession(reportBasePath, historyReader, marketLocator, scheduler);
            return simulationSession;
        }

        private Dictionary<string, ISimulationSession> GetSimulationSessions(string basePath, string reportBasePath, MarketMessageScheduler scheduler, Func<string, Market> marketLocator)
        {
            var result = new Dictionary<string, ISimulationSession>();
            result["Betfair"] = GetBetfairSimulationSession(basePath, reportBasePath, scheduler, marketLocator);
            result["Matchbook"] = GetMatchbookSimulationSession(basePath, reportBasePath, scheduler, marketLocator);
            return result;
        }

        private bool IsAMatch(MarketObservation first, MarketObservation second)
        {
            if (first.Start != second.Start) return false;
            var outcomeNames = new HashSet<string>(first.Outcomes.Select(o => o.Name));
            if (!second.Outcomes.All(o => outcomeNames.Contains(o.Name))) return false;
            outcomeNames = new HashSet<string>(second.Outcomes.Select(o => o.Name));
            if (!first.Outcomes.All(o => outcomeNames.Contains(o.Name))) return false;
            return true;
        }

        private Dictionary<string, MarketObservation>[] GetMatchingMarkets(Dictionary<string, ISimulationSession> sessions, string eventCategory, string marketType)
        {
            var markets = new Dictionary<string, MarketObservation[]>();
            foreach(var session in sessions)
            {
                var selectedCategory = session.Value.GetEventCategories().Single(c => c.Name == eventCategory);
                var exchangeMarkets = session.Value
                    .GetEvents(new[] { selectedCategory })
                    .SelectMany(ev => ev.Markets.Values)
                    .Where(m => m.Type == marketType)
                    .ToArray();

                markets[session.Key] = exchangeMarkets.Where(m => session.Value.HasStream(m.Id)).ToArray();
            }

            var keyedMarkets = markets.SelectMany(kvp => kvp.Value.Select(m => new { exchange = kvp.Key, marketKey = $"{m.Start}-{string.Join("-", m.Outcomes.OrderBy(i => i.Name))}", market = m })).ToArray();

            var result = new List<Dictionary<string, MarketObservation>>();
            foreach(var group in keyedMarkets.GroupBy(m => m.marketKey))
            {
                var items = group.ToArray();
                if(items.Length > 1)
                {
                    var matching = new Dictionary<string, MarketObservation>();
                    foreach(var item in items)
                    {
                        matching[item.exchange] = item.market;
                    }
                    if(matching.Count > 1)
                    {
                        result.Add(matching);
                    }
                }
            }

            return result.ToArray();
        }

        [TestMethod]
        public void RunHistoryDumpAgent()
        {
            var scheduler = new MarketMessageScheduler();
            var simulationDate = new DateTime(2019, 10, 13);
            var sessions = new Dictionary<string, ISimulationSession>();
            var markets = new Dictionary<string, Market>();
            var basePath = @"D:\Data\btrader";
            var reportBasePath = $@"{basePath}\reports\hfttests";
            foreach (var session in GetSimulationSessions(basePath, reportBasePath, scheduler, id => markets[id]))
            {
                session.Value.SimulationDate = simulationDate;
                sessions[session.Key] = session.Value;
            }

            var agentContext = new AgentContext(sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as ISession), Observable.Never<DateTime>(), null);
            var matching = GetMatchingMarkets(sessions, "Horse Racing", "WIN");
            var firstSet = matching.First();
            var primary = firstSet["Betfair"];
            var secondary = firstSet["Matchbook"];
            var primaryMarket = Market.FromObservation(primary, sessions["Betfair"].GetMarketChangeStream(primary.Id));
            var secondaryMarket = Market.FromObservation(secondary, sessions["Matchbook"].GetMarketChangeStream(secondary.Id));
            using(var agents = new CompositeDisposable())
            {
                foreach(var primaryOutcome in primaryMarket.Outcomes.Values)
                {
                    var seconaryOutcome = secondaryMarket.Outcomes.First(o => o.Value.Name == primaryOutcome.Name).Value;
                    var agent = new HistoryDumpAgent(agentContext, primaryMarket, primaryOutcome, secondaryMarket, seconaryOutcome);
                    agents.Add(agent);
                }

                scheduler.Run(CancellationToken.None).Wait(TimeSpan.FromMinutes(10));
            }
        }

        [TestMethod]
        public void RunTradeIntensityAgentSimulationParallel()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var pythonHome = @"C:\Users\adam\Work\BTrader\BTrader.Python\python3.6.1_x64\runtime\";
            var pythonPath = $@"{pythonHome};{pythonHome}python36.zip;{pythonHome}\Lib\site-packages;{exeDir};C:\Users\adam\Work\BTrader\BTrader.Python.Tests\";
            var pythonSession = new PythonSession(pythonHome, pythonPath, new Log());
            pythonSession.LoadModule(@"testfunctions").Wait();

            var basePath = this.GetBasePath() + @"\betfair";
            var dates = GetAvailableDates(basePath)
                .Where(x => x.DayOfWeek == DayOfWeek.Saturday | x.DayOfWeek == DayOfWeek.Sunday)
                .OrderByDescending(x => x).ToArray();

            dates.AsParallel()
                .WithDegreeOfParallelism(1)
                .Select(date =>
                {
                    RunTradeIntensityAgentSimulation(date, pythonSession);

                    return true;
                }).ToArray();
        }

        public void RunTradeIntensityAgentSimulation(DateTime simulationDate, PythonSession pythonSession)
        {
            var log = new DebugLogger();
            var scheduler = new MarketMessageScheduler();
            var sessions = new Dictionary<string, ISimulationSession>();
            var markets = new Dictionary<string, Market>();
            var basePath = GetBasePath();
            var reportBasePath = $@"{basePath}\reports\tradeintensitytests";
            if (!Directory.Exists(reportBasePath))
            {
                Directory.CreateDirectory(reportBasePath);
            }

            foreach (var session in GetSimulationSessions(basePath, reportBasePath, scheduler, id => markets[id]))
            {
                session.Value.SimulationDate = simulationDate;
                sessions[session.Key] = session.Value;
            }

            var matching = GetMatchingMarkets(sessions, "Horse Racing", "WIN");
            var i = 0;
            foreach (var marketSet in matching)
            {
                i++;
                log.Info($"running set {i} of {matching.Length}");
                var primary = marketSet["Betfair"];
                var primaryMarket = Market.FromObservation(primary, sessions["Betfair"].GetMarketChangeStream(primary.Id));
                markets[primaryMarket.Id] = primaryMarket;
                using (var disposable = new CompositeDisposable())
                {
                    var agents = new List<TradeIntensityAgent>();
                    foreach (var primaryOutcome in primaryMarket.Outcomes.Values)
                    {
                        var logFile = $@"{reportBasePath}\tradeintensityagent-{primaryMarket.Id}-{primaryOutcome.Id}.csv";
                        var agentContext = new AgentContext(sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as ISession), Observable.Never<DateTime>(), logFile);
                        var agent = new TradeIntensityAgent(agentContext, primaryMarket, primaryOutcome, pythonSession);
                        disposable.Add(agentContext);
                        disposable.Add(agent);
                        agents.Add(agent);
                        agent.State = AgentState.Running;
                    }

                    var cancellationSource = new CancellationTokenSource();
                    var runTask = scheduler.Run(cancellationSource.Token);
                    runTask.Wait(TimeSpan.FromMinutes(30));
                    cancellationSource.Cancel();
                    runTask.Wait(TimeSpan.FromMinutes(1));
                    foreach (var agent in agents)
                    {
                        log.Info($"{agent.Id} pnl: {agent.PnL}");
                    }
                }

                scheduler.Reset();
            }
        }

        [TestMethod]
        public void RunHFTAgentSimulationParallel()
        {
            var basePath = this.GetBasePath() + @"\betfair";
            var dates = GetAvailableDates(basePath).ToArray();

            dates.AsParallel()
                .WithDegreeOfParallelism(6)
                .Select(date =>
                {
                    RunHFTAgentSimulation(date);

                    return true;
                }).ToArray();
        }

        public void RunHFTAgentSimulation(DateTime simulationDate)
        {
            var log = new DebugLogger();
            var scheduler = new MarketMessageScheduler();
            var sessions = new Dictionary<string, ISimulationSession>();
            var markets = new Dictionary<string, Market>();
            var basePath = GetBasePath();
            var reportBasePath = $@"{basePath}\reports\hfttests_v12";
            if (!Directory.Exists(reportBasePath))
            {
                Directory.CreateDirectory(reportBasePath);
            }

            var rulesPath = @"C:\Users\adam\Work\BTrader\BTrader.Algo\HFTRules_v12.csv";
            foreach (var session in GetSimulationSessions(basePath, reportBasePath, scheduler, id => markets[id]))
            {
                session.Value.SimulationDate = simulationDate;
                sessions[session.Key] = session.Value;
            }

            var matching = GetMatchingMarkets(sessions, "Horse Racing", "WIN");
            var i = 0;
            foreach (var marketSet in matching)
            {
                i++;
                log.Info($"running set {i} of {matching.Length}");
                var primary = marketSet["Betfair"];
                var primaryMarket = Market.FromObservation(primary, sessions["Betfair"].GetMarketChangeStream(primary.Id));
                markets[primaryMarket.Id] = primaryMarket;
                using (var disposable = new CompositeDisposable())
                {
                    var agents = new List<HFTAgent>();
                    foreach (var primaryOutcome in primaryMarket.Outcomes.Values)
                    {
                        var logFile = $@"{reportBasePath}\hftagent-{primaryMarket.Id}-{primaryOutcome.Id}.csv";
                        var agentContext = new AgentContext(sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as ISession), Observable.Never<DateTime>(), logFile);
                        var agent = new HFTAgent(agentContext, rulesPath, primaryMarket, primaryOutcome);
                        disposable.Add(agentContext);
                        disposable.Add(agent);
                        agents.Add(agent);
                        agent.State = AgentState.Running;
                    }

                    var cancellationSource = new CancellationTokenSource();
                    var runTask = scheduler.Run(cancellationSource.Token);
                    runTask.Wait(TimeSpan.FromMinutes(30));
                    cancellationSource.Cancel();
                    runTask.Wait(TimeSpan.FromMinutes(1));
                    foreach(var agent in agents)
                    {
                        log.Info($"{agent.Id} pnl: {agent.PnL}");
                    }
                }

                scheduler.Reset();
            }
        }

        private string GetBasePath()
        {
            var choices = new[] 
            {
                @"c:\users\adam\data\btrader",
                @"D:\Data\btrader"
            };

            foreach(var choice in choices)
            {
                if (Directory.Exists(choice)) return choice;
            }

            throw new ApplicationException("Could not identify base path");
        }

        private IEnumerable<DateTime> GetAvailableDates(string path)
        {
            var dirs = Directory.GetDirectories(path).Select(s => s.Split('\\').Last());
            foreach(var d in dirs)
            {
                yield return DateTime.ParseExact(d, "yyyyMMdd", CultureInfo.CurrentUICulture);
            }
        }

        [TestMethod]
        public void RunHFTAgentDualSimulationParallel()
        {
            var basePath = $@"{GetBasePath()}\betfair";
            var dates = GetAvailableDates(basePath).ToArray();
            dates.AsParallel()
                .WithDegreeOfParallelism(6)
                .Select(date =>
            {
                RunHFTAgentDualSimulation(date);
                return true;
            }).ToArray();
        }

        public void RunHFTAgentDualSimulation(DateTime simulationDate)
        {
            var log = new DebugLogger();
            var scheduler = new MarketMessageScheduler();
            var sessions = new Dictionary<string, ISimulationSession>();
            var markets = new Dictionary<string, Market>();
            var basePath = GetBasePath();
            var reportBasePath = $@"{basePath}\reports\hfttests_dual";
            var rulesPath = @"C:\Users\adam\Work\BTrader\BTrader.Algo\HFTRules_v2.csv";
            foreach (var session in GetSimulationSessions(basePath, reportBasePath, scheduler, id => markets[id]))
            {
                session.Value.SimulationDate = simulationDate;
                sessions[session.Key] = session.Value;
            }

            var matching = GetMatchingMarkets(sessions, "Horse Racing", "WIN");
            var i = 0;
            foreach (var marketSet in matching)
            {
                i++;
                log.Info($"running set {i} of {matching.Length}");
                var primary = marketSet["Betfair"];
                var secondary = marketSet["Matchbook"];
                var primaryMarket = Market.FromObservation(primary, sessions["Betfair"].GetMarketChangeStream(primary.Id));
                var secondaryMarket = Market.FromObservation(secondary, sessions["Matchbook"].GetMarketChangeStream(secondary.Id));
                markets[primaryMarket.Id] = primaryMarket;
                using (var disposable = new CompositeDisposable())
                {
                    var agents = new List<HFTAgentDual>();
                    foreach (var primaryOutcome in primaryMarket.Outcomes.Values)
                    {
                        var seconaryOutcome = secondaryMarket.Outcomes.Values.FirstOrDefault(o => o.Name == primaryOutcome.Name);
                        if (seconaryOutcome == null) continue;
                        var logFile = $@"{reportBasePath}\hftagent-{primaryMarket.Id}-{primaryOutcome.Id}.csv";
                        var agentContext = new AgentContext(sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as ISession), Observable.Never<DateTime>(), logFile);
                        var agent = new HFTAgentDual(agentContext, rulesPath, primaryMarket, primaryOutcome, seconaryOutcome);
                        disposable.Add(agentContext);
                        disposable.Add(agent);
                        agents.Add(agent);
                    }

                    var cancellationSource = new CancellationTokenSource();
                    var runTask = scheduler.Run(cancellationSource.Token);
                    runTask.Wait(TimeSpan.FromMinutes(30));
                    cancellationSource.Cancel();
                    runTask.Wait(TimeSpan.FromMinutes(1));
                    foreach (var agent in agents)
                    {
                        log.Info($"{agent.Id} pnl: {agent.PnL}");
                    }
                }

                scheduler.Reset();
            }
        }

    }
}
