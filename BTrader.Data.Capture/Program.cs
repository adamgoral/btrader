using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Data.Capture
{
    class Program
    {
        private static IObservable<Market> GetActiveMarkets(ISession session, string categoryName, string marketType, int minutesBeforeStart)
        {
            var eventCategory = session.GetEventCategories().Single(e => e.Name == categoryName);
            var knownMarkets = new Dictionary<string, MarketObservation>();
            var notifiedMarkets = new HashSet<string>();
            var newMarkets = new Subject<Market>();
            var lastCheck = DateTime.Now.AddHours(-1);
            Observable
                .Timer(DateTimeOffset.Now, TimeSpan.FromSeconds(1))
                .Subscribe(i =>
            {
                try
                {
                    if (lastCheck < DateTime.Now.AddHours(-1))
                    {
                        lastCheck = DateTime.Now;
                        var markets = session.GetEvents(new[] { eventCategory })
                                             .SelectMany(e => e.Markets.Values)
                                             .ToArray();
                        foreach (var market in markets.Where(m => m.Type == marketType))
                        {
                            if (!knownMarkets.ContainsKey(market.Id))
                            {
                                knownMarkets[market.Id] = market;
                            }
                        }
                    }

                    foreach (var market in knownMarkets.Where(kvp => !notifiedMarkets.Contains(kvp.Key)).ToArray())
                    {
                        if (market.Value.Start != null)
                        {
                            if (market.Value.Start.Value.AddMinutes(-minutesBeforeStart) < DateTime.UtcNow && DateTime.UtcNow < market.Value.Start.Value)
                            {
                                notifiedMarkets.Add(market.Key);
                                newMarkets.OnNext(Market.FromObservation(market.Value, session.GetMarketChangeStream(market.Key)));
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Error getting active markets {ex}");
                }
            });

            return newMarkets;
        }

        static void Main(string[] args)
        {
            var betfairSession = Betfair.Session.Create();
            var matchbookSession = Matchbook.Session.Create();
            var eventCategory = "Horse Racing";
            var marketType = "WIN";
            var minutesBeforeStart = 60;
            var activeMarkets = Observable
                .Merge(GetActiveMarkets(betfairSession, eventCategory, marketType, minutesBeforeStart),
                       GetActiveMarkets(matchbookSession, eventCategory, marketType, minutesBeforeStart));
            using (var activeMarketsSub = activeMarkets.Subscribe(market =>
             {
                 Console.WriteLine($"Market has become active {market.Id}");
                 IDisposable marketSub = null;
                 marketSub = market.Changes.Subscribe(o => 
                 {
                     if(o.Status == MarketStatus.Closed)
                     {
                         Console.WriteLine($"Market has closed {market.Id}");
                         marketSub?.Dispose();
                     }
                 });
             }))
            {
                Console.ReadLine();
            }

            Environment.Exit(0);
        }
    }
}
