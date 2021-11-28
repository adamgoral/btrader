using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;

namespace BTrader.Algo
{
    public class HistoryDumpAgent : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly AgentContext context;
        private readonly Outcome primaryBook;
        private readonly Outcome supportingBook;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly ILog log = new DebugLogger();

        public HistoryDumpAgent(AgentContext context, Market primaryMarket, Outcome primaryBook, Market supportingMarket, Outcome supportingBook)
        {
            this.writer = new StreamWriter($@"C:\Users\adam\Data\btrader\reports\{primaryMarket.Id}-{primaryBook.Id}.csv", false);
            this.writer.WriteLine("timestamp,size,back,lay,size,size,back,lay,size");
            this.context = context;
            this.primaryBook = primaryBook;
            this.supportingBook = supportingBook;
            this.disposables.Add(context.Timer.Subscribe(this.OnTimer));
            this.disposables.Add(primaryBook.Changes.Subscribe(c => this.OnOutcomeChange("primary", c)));
            this.disposables.Add(supportingBook.Changes.Subscribe(c => this.OnOutcomeChange("secondary", c)));
        }

        private PriceSize? GetBestToLay(OrderBook orderBook)
        {
            return orderBook.ToLay.OrderBy(o => o.Key).Select(o => new PriceSize(o.Key, o.Value)).FirstOrDefault();
        }

        private PriceSize? GetBestToBack(OrderBook orderBook)
        {
            return orderBook.ToBack.OrderByDescending(o => o.Key).Select(o => new PriceSize(o.Key, o.Value)).FirstOrDefault();
        }

        private void OnOutcomeChange(string exchange, OutcomeChange outcomeChange)
        {
            lock (this)
            {
                var primary = this.primaryBook.OrderBook.Clone();
                var secondary = this.supportingBook.OrderBook.Clone();
                var bestToLay = primary.ToLay.Union(secondary.ToLay).GroupBy(p => Math.Round(p.Key, 2)).Select(g => Tuple.Create(g.Key, g.Sum(kvp => kvp.Value))).OrderBy(p => p.Item1).FirstOrDefault();
                var bestToBack = primary.ToBack.Union(secondary.ToBack).GroupBy(p => Math.Round(p.Key, 2)).Select(g => Tuple.Create(g.Key, g.Sum(kvp => kvp.Value))).OrderByDescending(p => p.Item1).FirstOrDefault();
                if (bestToLay != null && bestToBack != null)
                {
                    var pBestToLay = GetBestToLay(primary);
                    var pBestToBack = GetBestToBack(primary);
                    var sBestToLay = GetBestToLay(secondary);
                    var sBestToBack = GetBestToBack(secondary);
                    this.writer.WriteLine(string.Join(",", new[] {
                    outcomeChange.Timestamp.ToString("dd/MM/yyyy hh:mm:ss.fff"),
                    pBestToBack?.Size.ToString(),
                    pBestToBack?.Price.ToString(),
                    pBestToLay?.Price.ToString(),
                    pBestToLay?.Size.ToString(),
                    sBestToBack?.Size.ToString(),
                    sBestToBack?.Price.ToString(),
                    sBestToLay?.Price.ToString(),
                    sBestToLay?.Size.ToString(),
                }));
                    this.writer.Flush();
                    var arbitrageIndicator = "";
                    if (bestToLay.Item1 < bestToBack.Item1)
                    {
                        arbitrageIndicator = "ARBITRAGE";
                    }

                    var depthImballance = Math.Log((double)bestToBack.Item2) - Math.Log((double)bestToLay.Item2);
                    //this.log.Info($"OnOutcomeChange {outcomeChange.Timestamp} - {exchange} - {outcomeChange.Id} {arbitrageIndicator} {depthImballance} {bestToBack.Item2}@{bestToBack.Item1} {bestToLay.Item2}@{bestToLay.Item1}");
                }
            }
        }

        private void OnTimer(DateTime dateTime)
        {
            this.log.Info($"OnTimer {dateTime}");
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}
