using BTrader.Algo.HFTRules;
using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.ComponentModel;

namespace BTrader.Algo
{

    public class HFTAgent : IDisposable, INotifyPropertyChanged
    {
        private readonly AgentContext context;
        private readonly ISession session;
        private readonly Market market;
        private readonly Outcome outcome;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly ILog log = new DebugLogger();
        private Dictionary<Tuple<double, double>, Dictionary<Tuple<double, double>, HFTRule>> Rules { get; }
        private readonly int depth = 3;
        private readonly decimal tradeSize = 2;
        private readonly decimal minOrderSize = 2;
        private DateTime lastOrderTimestamp = DateTime.MinValue;
        private TimeSpan delayBeforeNextOrder = TimeSpan.FromMilliseconds(500);
        private TimeSpan exitTimeBeforeEventStart = TimeSpan.FromMinutes(5);
        private AgentState _state;
        private readonly object updateLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChagned(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Id { get; }

        public AgentState State
        {
            get => _state;
            set
            {
                if(_state != value)
                {
                    _state = value;
                    this.OnPropertyChagned("State");
                }
            }
        }

        public HFTAgent(AgentContext context, string rulesPath, Market market, Outcome outcome)
        {
            this.context = context;
            this.session = context.Sessions["Betfair"];
            this.market = market;
            this.outcome = outcome;
            this.Id = $"HFT-{market.Id}-{outcome.Id}";
            this.Rules = HFTRule.LoadRules(rulesPath);
            this.disposables.Add(context.Timer.Subscribe(this.OnTimer));
            this.disposables.Add(outcome.Changes
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
            this.disposables.Add(outcome.Changes
                .Where(o => o.Orders.Count != 0)
                .Subscribe(c =>
                {
                    this.OnOrderUpdate(c.Timestamp, c.Orders);
                }));

            this.context.Log(string.Join(",", new[]
            {
                "OnOutcomeChange",
                "timestamp",
                "laySize",
                "layPrice",
                "backPrice",
                "backSize",
                "depthImbalance",
                "position",
                "filledOrders",
                "layOrders",
                "backOrders",
                "rule",
                "pnl",
                "status"
            }));
            this.context.Log(string.Join(",", new[]
            {
                "OnOrderUpdate",
                "timestamp",
                "id",
                "marketId",
                "outcomeId",
                "status",
                "side",
                "size",
                "price",
                "sizeFilled"
            }));
        }

        private void OnOrderUpdate(DateTime timestamp, IEnumerable<Order> orders)
        {
            
            foreach(var order in orders)
            {
                this.context.Log(string.Join(",", new[]
                {
                    "OnOrderUpdate",
                    timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    order.Id,
                    order.MarketId,
                    order.OutcomeId,
                    order.Status.ToString(),
                    order.Side.ToString(),
                    order.Size.ToString(),
                    order.Price.ToString(),
                    order.SizeFilled.ToString()
                }));
            }

            var openOrders = this.outcome.Orders.Values.Where(o => o.Status == OrderStatus.Open).ToArray();
            this.UpdateOrdersCounts(openOrders);
        }

        public decimal PnL { get; private set; }
        public decimal Inventory { get; private set; }
        public double DepthImbalance { get; private set; }
        public decimal BackOrderVolume { get; private set; }
        public decimal LayOrderVolume { get; private set; }

        public decimal BackOrderCount { get; private set; }
        public decimal LayOrderCount { get; private set; }

        public HFTRule Rule { get; private set; }

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

        private AgentPriceSize[] GetBestPrices(IOrderedEnumerable<KeyValuePair<decimal, decimal>> orderedMarket, int depth, IEnumerable<Order> openOrders)
        {
            var ordersLookup = openOrders
                .GroupBy(o => o.Price)
                .Select(g => new { price = g.Key, size = g.Sum(o => o.Size - o.SizeFilled) })
                .ToDictionary(o => o.price, o => o.size);

            return orderedMarket
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

        private AgentPriceSize[] GetBestToLay(OrderBook orderBook, int depth, Order[] orders)
        {
            var backOrders = orders.Where(o => o.Status == OrderStatus.Open && o.Side == OrderSide.Back);
            var orderedMarket = orderBook.ToLay.OrderBy(o => o.Key);
            return GetBestPrices(orderedMarket, depth, backOrders);
        }

        private AgentPriceSize[] GetBestToBack(OrderBook orderBook, int depth, Order[] orders)
        {
            var layOrders = orders.Where(o => o.Status == OrderStatus.Open && o.Side == OrderSide.Lay);
            var orderedMarket = orderBook.ToBack.OrderByDescending(o => o.Key);
            return GetBestPrices(orderedMarket, depth, layOrders);
        }

        private readonly object ordersCountLock = new object();

        private void UpdateOrdersCounts(Order[] openOrders)
        {
            lock (this.ordersCountLock)
            {
                this.BackOrderVolume = openOrders.Where(o => o.Side == OrderSide.Back).Sum(o => o.Size - o.SizeFilled);
                this.OnPropertyChagned("BackOrderVolume");
                this.BackOrderCount = openOrders.Where(o => o.Side == OrderSide.Back).Count();
                this.OnPropertyChagned("BackOrderCount");
                this.LayOrderVolume = openOrders.Where(o => o.Side == OrderSide.Lay).Sum(o => o.Size - o.SizeFilled);
                this.OnPropertyChagned("LayOrderVolume");
                this.LayOrderCount = openOrders.Where(o => o.Side == OrderSide.Lay).Count();
                this.OnPropertyChagned("LayOrderCount");
            }
        }

        private void OnOutcomeChange(string id, OutcomeChange change)
        {
            var newOrders = new List<OrderRequest>();
            var clearOrders = new List<Order>();
            var orders = this.outcome.Orders.Values;
            var openOrders = orders.Where(o => o.Status == OrderStatus.Open).ToArray();
            this.UpdateOrdersCounts(openOrders);

            if (change.Timestamp > this.market.Start - this.exitTimeBeforeEventStart)
            {
                this.State = AgentState.Stopped;
            }

            if (this.State == AgentState.Stopped)
            {
                foreach(var order in openOrders)
                {
                    this.context.Log(string.Join(",", new[]
                    {
                        "cancelOrder",
                        change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        order.Id
                    }));
                }

                session.CancelOrders(openOrders);
            }

            var book = this.outcome.OrderBook.Clone();
            var bestToLayArray = this.GetBestToLay(book, this.depth, openOrders);
            var bestToBackArray = this.GetBestToBack(book, this.depth, openOrders);
            if (bestToLayArray.Length >= this.depth && bestToBackArray.Length >= this.depth)
            {
                var bestToBack = bestToBackArray.First();
                var bestToLay = bestToLayArray.First();
                var depthImbalance = Math.Log((double)bestToBack.Market.Size) - Math.Log((double)bestToLay.Market.Size);
                depthImbalance = Math.Round(depthImbalance, 2);
                this.DepthImbalance = depthImbalance;
                this.OnPropertyChagned("DepthImbalance");
                var filledOrders = orders.Where(o => o.Status == OrderStatus.Filled).ToArray();
                var inventory = 0M;
                inventory -= filledOrders.Where(o => o.Side == OrderSide.Back).Sum(o => o.SizeFilled * o.Price);
                inventory += filledOrders.Where(o => o.Side == OrderSide.Lay).Sum(o => o.SizeFilled * o.Price);
                inventory = inventory / (inventory >= 0 ? bestToBack.Market.Price : bestToLay.Market.Price);

                inventory = Math.Round(inventory, 2);
                this.Inventory = inventory;
                this.OnPropertyChagned("Inventory");
                var rule = this.LookupRule(depthImbalance, (double)inventory);
                this.Rule = rule;
                this.OnPropertyChagned("Rule");

                var pnl = this.outcome.CalculatePnl(bestToBack.Market.Price, bestToLay.Market.Price);
                pnl = Math.Round(pnl, 4);
                this.PnL = pnl;
                this.OnPropertyChagned("PnL");
                this.context.Log(string.Join(",", new[]
                {
                        "OnOutcomeChange",
                        change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        bestToBack.Market.Size.ToString(),
                        bestToBack.Market.Price.ToString(),
                        bestToLay.Market.Price.ToString(),
                        bestToLay.Market.Size.ToString(),
                        depthImbalance.ToString(),
                        inventory.ToString(),
                        filledOrders.Length.ToString(),
                        this.LayOrderVolume.ToString(),
                        this.BackOrderVolume.ToString(),
                        rule.ToString(),
                        pnl.ToString(),
                        this.State.ToString()
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

            if (this.State == AgentState.Running)
            {
                foreach(var order in clearOrders)
                {
                    this.context.Log(string.Join(",", new[]
                    {
                        "cancelOrder",
                        change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        order.Id
                    }));
                }

                this.session.CancelOrders(clearOrders);

                if (newOrders.Any())
                {
                    // do not place orders until delay
                    if (this.lastOrderTimestamp < change.Timestamp - this.delayBeforeNextOrder)
                    {
                        foreach(var order in newOrders)
                        {
                            this.context.Log(string.Join(",", new[]
                            {
                                "placeOrder",
                                change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                                order.MarketId,
                                order.OutcomeId,
                                order.Side.ToString(),
                                order.Size.ToString(),
                                order.Price.ToString()
                            }));
                        }

                        this.session.PlaceOrders(newOrders);
                        this.lastOrderTimestamp = change.Timestamp;
                    }
                }
            }
        }

        private void OnTimer(DateTime dateTime)
        {
            this.log.Info($"OnTimer {dateTime}");
        }

        public void Dispose()
        {
            this.State = AgentState.Stopped;
            this.disposables.Dispose();
        }
    }
}
