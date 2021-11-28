using BTrader.Algo.HFTRules;
using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading;

namespace BTrader.Algo
{
    public class HFTAgentDual : IDisposable
    {
        private readonly AgentContext context;
        private readonly ISession session;
        private readonly Market market;
        private readonly Outcome outcome;
        private readonly Outcome outcomeAltExchange;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly ILog log = new DebugLogger();
        private Dictionary<Tuple<double, double>, Dictionary<Tuple<double, double>, HFTRule>> Rules { get; }
        private readonly int depth = 3;
        private readonly decimal tradeSize = 2;
        private readonly decimal minOrderSize = 2;
        private DateTime lastOrderTimestamp = DateTime.MinValue;
        private TimeSpan delayBeforeNextOrder = TimeSpan.FromMilliseconds(200);
        private TimeSpan exitTimeBeforeEventStart = TimeSpan.FromMinutes(5);
        private readonly object updateLock = new object();

        public string Id { get; }

        public HFTAgentDual(AgentContext context, string rulesPath, Market market, Outcome outcome, Outcome outcomeAltExchange)
        {
            this.context = context;
            this.session = context.Sessions["Betfair"];
            this.market = market;
            this.outcome = outcome;
            this.outcomeAltExchange = outcomeAltExchange;
            this.Id = $"HFTDual-{market.Id}-{outcome.Id}-{outcomeAltExchange.Id}";
            this.Rules = HFTRule.LoadRules(rulesPath);
            this.disposables.Add(context.Timer.Subscribe(this.OnTimer));
            var changes = Observable.Merge(outcome.Changes, outcomeAltExchange.Changes);
            this.disposables.Add(changes
                .Where(o => o.Orders.Count == 0)
                .Subscribe(c =>
            {
                if (!Monitor.TryEnter(this.updateLock)) return;
                try
                {
                    this.OnOutcomeChange(outcome.Id, c);
                }
                finally
                {
                    Monitor.Exit(this.updateLock);
                }
            }));
            this.context.Log(string.Join(",", new[]
            {
                "timestamp",
                "laySize",
                "layPrice",
                "backPrice",
                "backSize",
                "depthImbalance",
                "position",
                "filledOrders",
                "openOrders",
                "rule",
                "pnl"
            }));
        }

        public decimal PnL { get; private set; }

        private HFTRule LookupRule(double depthImbalance, double inventory)
        {
            foreach (var row in this.Rules)
            {
                if (row.Key.Item1 <= depthImbalance && depthImbalance < row.Key.Item2)
                {
                    foreach (var cell in row.Value)
                    {
                        if (cell.Key.Item1 <= inventory && inventory < cell.Key.Item2)
                        {
                            return cell.Value;
                        }
                    }
                }
            }

            throw new ApplicationException($"Could not locate HFT rule for depthImbalance: {depthImbalance}, inventory: {inventory}");
        }

        private AgentPriceSize[] GetBestPrices(IOrderedEnumerable<KeyValuePair<decimal, decimal>> orderedMarket, int depth, IEnumerable<Order> openOrders, IOrderedEnumerable<KeyValuePair<decimal, decimal>> orderedAlt)
        {
            var ordersLookup = openOrders
                .GroupBy(o => o.Price)
                .Select(g => new { price = g.Key, size = g.Sum(o => o.Size - o.SizeFilled) })
                .ToDictionary(o => o.price, o => o.size);
            var primarySet = new HashSet<decimal>(orderedMarket.Select(k => k.Key));

            var merged = orderedMarket.Union(orderedAlt.Where(k => primarySet.Contains(k.Key)))
                            .GroupBy(o => o.Key)
                            .Select(g => new KeyValuePair<decimal, decimal>(g.Key, g.Sum(i => i.Value)));

            return merged
                .Select(o =>
                {
                    var ps = new PriceSize(o.Key, o.Value);
                    decimal agentSize;
                    ordersLookup.TryGetValue(ps.Price, out agentSize);
                    return new AgentPriceSize(ps, agentSize);
                })
                .Take(depth)
                .ToArray();
        }

        private AgentPriceSize[] GetBestToLay(OrderBook orderBook, int depth, Order[] orders, OrderBook bookAlt)
        {
            var backOrders = orders.Where(o => o.Status == OrderStatus.Open && o.Side == OrderSide.Back);
            var orderedMarket = orderBook.ToLay.OrderBy(o => o.Key);
            var orderedAlt = bookAlt.ToLay.OrderBy(o => o.Key);
            return GetBestPrices(orderedMarket, depth, backOrders, orderedAlt);
        }

        private AgentPriceSize[] GetBestToBack(OrderBook orderBook, int depth, Order[] orders, OrderBook bookAlt)
        {
            var layOrders = orders.Where(o => o.Status == OrderStatus.Open && o.Side == OrderSide.Lay);
            var orderedMarket = orderBook.ToBack.OrderByDescending(o => o.Key);
            var orderedAlt = bookAlt.ToBack.OrderByDescending(o => o.Key);
            return GetBestPrices(orderedMarket, depth, layOrders, orderedAlt);
        }

        private void OnOutcomeChange(string id, OutcomeChange change)
        {
            var newOrders = new List<OrderRequest>();
            var clearOrders = new List<Order>();

            var orders = this.outcome.Orders.Values.ToArray();
            var openOrders = orders.Where(o => o.Status == OrderStatus.Open).ToArray();
            if (change.Timestamp > this.market.Start - this.exitTimeBeforeEventStart)
            {
                session.CancelOrders(openOrders);
                return;
            }

            var book = this.outcome.OrderBook.Clone();
            var bookAlt = this.outcomeAltExchange.OrderBook.Clone();
            var bestToLayArray = this.GetBestToLay(book, this.depth, openOrders, bookAlt);
            var bestToBackArray = this.GetBestToBack(book, this.depth, openOrders, bookAlt);
            if (bestToLayArray.Length >= this.depth && bestToBackArray.Length >= this.depth)
            {
                var bestToBack = bestToBackArray.First();
                var bestToLay = bestToLayArray.First();
                var depthImbalance = Math.Log((double)bestToBack.Market.Size) - Math.Log((double)bestToLay.Market.Size);
                depthImbalance = Math.Round(depthImbalance, 2);
                var filledOrders = orders.Where(o => o.Status == OrderStatus.Filled).ToArray();
                var inventory = 0M;
                inventory -= filledOrders.Where(o => o.Side == OrderSide.Back).Sum(o => o.SizeFilled * o.Price);
                inventory += filledOrders.Where(o => o.Side == OrderSide.Lay).Sum(o => o.SizeFilled * o.Price);
                inventory = inventory / (inventory >= 0 ? bestToBack.Market.Price : bestToLay.Market.Price);

                inventory = Math.Round(inventory, 2);
                var rule = this.LookupRule(depthImbalance, (double)inventory);

                var pnl = this.outcome.CalculatePnl(bestToBack.Market.Price, bestToLay.Market.Price);
                pnl = Math.Round(pnl, 4);
                this.PnL = pnl;
                this.context.Log(string.Join(",", new[]
                {
                        change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        bestToBack.Market.Size.ToString(),
                        bestToBack.Market.Price.ToString(),
                        bestToLay.Market.Price.ToString(),
                        bestToLay.Market.Size.ToString(),
                        depthImbalance.ToString(),
                        inventory.ToString(),
                        filledOrders.Length.ToString(),
                        openOrders.Length.ToString(),
                        rule.ToString(),
                        pnl.ToString()
                    }));

                if (rule is HFTRuleMarketMaking)
                {
                    var mm = rule as HFTRuleMarketMaking;
                    for (var i = 0; i < this.depth; i++)
                    {
                        var bestToBackAtDepth = bestToBackArray[i];
                        if (bestToBackAtDepth != null)
                        {
                            if (mm.LowerSpreadDistance == i + 1)
                            {
                                var tradeSize = this.tradeSize;
                                if (inventory < 0)
                                {
                                    tradeSize = Math.Max(this.tradeSize, Math.Abs(inventory));
                                }

                                tradeSize = tradeSize - bestToBackAtDepth.AgentSize;
                                if (tradeSize >= this.minOrderSize)
                                {
                                    var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Lay, bestToBackAtDepth.Market.Price, tradeSize);
                                    newOrders.Add(newOrder);
                                }
                            }
                            else if (mm.LowerSpreadDistance != i + 1 && bestToBackAtDepth.AgentSize != 0)
                            {
                                var existingOrders = openOrders.Where(o => o.Side == OrderSide.Lay && o.Price == bestToBackAtDepth.Market.Price).ToArray();
                                clearOrders.AddRange(existingOrders);
                            }
                        }

                        var bestToLayAtDepth = bestToLayArray[i];
                        if (bestToLayAtDepth != null)
                        {
                            if (mm.UpperSpreadDistance == i + 1)
                            {
                                var tradeSize = this.tradeSize;
                                if (inventory > 0)
                                {
                                    tradeSize = Math.Max(this.tradeSize, Math.Abs(inventory));
                                }

                                tradeSize = tradeSize - bestToLayAtDepth.AgentSize;
                                if (tradeSize >= this.minOrderSize)
                                {
                                    var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Back, bestToLayAtDepth.Market.Price, tradeSize);
                                    newOrders.Add(newOrder);
                                }
                            }
                            else if (mm.UpperSpreadDistance != i + 1 && bestToLayAtDepth.AgentSize != 0)
                            {
                                var existingOrders = openOrders.Where(o => o.Side == OrderSide.Back && o.Price == bestToLayAtDepth.Market.Price).ToArray();
                                clearOrders.AddRange(existingOrders);
                            }
                        }
                    }
                }
                else if (rule is HFTRuleInventoryControl || rule is HFTRulePartialInventoryControl)
                {
                    clearOrders.AddRange(openOrders);
                    var tradeSize = Math.Round(Math.Abs(inventory), 2);
                    if (rule is HFTRulePartialInventoryControl)
                    {
                        tradeSize = Math.Abs(this.tradeSize);
                    }

                    if (inventory >= this.tradeSize)
                    {
                        var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Back, bestToBack.Market.Price, tradeSize);
                        newOrders.Add(newOrder);
                    }
                    else if (inventory <= -this.tradeSize)
                    {
                        var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Lay, bestToLay.Market.Price, tradeSize);
                        newOrders.Add(newOrder);
                    }
                }
            }
            this.session.CancelOrders(clearOrders);

            if (newOrders.Any())
            {
                // throttle order dispatch to give incomming order stream to catch up
                // This should help avoiding sending duplicate orders in situations when previous order acknowledgement has not arrived yet
                if (this.lastOrderTimestamp < change.Timestamp - this.delayBeforeNextOrder)
                {
                    this.session.PlaceOrders(newOrders);
                    this.lastOrderTimestamp = change.Timestamp;
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
