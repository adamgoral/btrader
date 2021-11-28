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

    public class TradeIntensityAgent : IDisposable, INotifyPropertyChanged
    {
        private OrderBook previousBook;
        private readonly AgentContext context;
        private readonly ISession session;
        private readonly Market market;
        private readonly Outcome outcome;
        private readonly BTrader.Python.PythonSession pythonSession;
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
        private List<DateTime> backOrderArrivals = new List<DateTime>();
        private List<DateTime> layOrderArrivals = new List<DateTime>();
        private readonly DebugLogger debugLogger = new DebugLogger();

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
                if (_state != value)
                {
                    _state = value;
                    this.OnPropertyChagned("State");
                }
            }
        }

        public TradeIntensityAgent(AgentContext context, Market market, Outcome outcome, BTrader.Python.PythonSession pythonSession)
        {
            this.context = context;
            this.session = context.Sessions["Betfair"];
            this.market = market;
            this.outcome = outcome;
            this.pythonSession = pythonSession;
            this.Id = $"HFT-{market.Id}-{outcome.Id}";
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
                "totalTraded",
                "depthImbalance",
                "position",
                "filledOrders",
                "layOrders",
                "backOrders",
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

            foreach (var order in orders)
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
        public decimal TotalTraded { get; private set; }

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

        private PriceSizeChange[] GetTraded(OrderBook book)
        {
            var result = new List<PriceSizeChange>();
            if (this.previousBook != null)
            {
                foreach(var trade in book.Traded)
                {
                    decimal existing;
                    if(this.previousBook.Traded.TryGetValue(trade.Key, out existing))
                    {
                        if (existing != trade.Value)
                        {
                            result.Add(new PriceSizeChange(trade.Key, existing, trade.Value));
                        }
                    }
                    else
                    {
                        result.Add(new PriceSizeChange(trade.Key, 0, trade.Value));
                    }
                }
            }

            this.previousBook = book;
            return result.ToArray();
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
                foreach (var order in openOrders)
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
                var midPrice = (bestToBack.Market.Price + bestToLay.Market.Price) * 0.5M;
                var tradeChanges = this.GetTraded(book);
                foreach (var traded in tradeChanges)
                {
                    var tradedPrice = traded.Price;
                    if(tradedPrice > midPrice)
                    {
                        if (!this.layOrderArrivals.Contains(change.Timestamp))
                        {
                            this.layOrderArrivals.Add(change.Timestamp);
                        }
                    }
                    else if(tradedPrice < midPrice)
                    {
                        if (!this.backOrderArrivals.Contains(change.Timestamp))
                        {
                            this.backOrderArrivals.Add(change.Timestamp);
                        }
                    }
                }

                var lookback = TimeSpan.FromMinutes(5);
                var minArrival = change.Timestamp - lookback;
                var layArrivals = this.layOrderArrivals.Where(x => x > minArrival).ToArray();
                var backArrivals = this.backOrderArrivals.Where(x => x > minArrival).ToArray();

                if(layArrivals.Length > 20 && backArrivals.Length > 20)
                {
                    //this.log.Info($"{layArrivals} {backArrivals}");
                    var layArrivalTimeSpans = layArrivals.Select(x => x - minArrival).ToArray();
                    var backArrivalTimeSpans = backArrivals.Select(x => x - minArrival).ToArray();
                    var layArrivalSeconds = layArrivalTimeSpans.Select(x => Math.Round(x.TotalSeconds, 3)).OrderBy(x=>x).ToArray();
                    var backArrivalSeconds = backArrivalTimeSpans.Select(x => Math.Round(x.TotalSeconds, 3)).OrderBy(x=>x).ToArray();
                    try
                    {
                        var insensityRatio = pythonSession.CallModuleFunction<double>("testfunctions", "get_intensity_ratio", 1.0, layArrivalSeconds.ToList(), backArrivalSeconds.ToList()).Result;
                        insensityRatio = Math.Round(insensityRatio, 2);
                        this.DepthImbalance = insensityRatio;
                        this.OnPropertyChagned("DepthImbalance");
                    }
                    catch (Exception ex)
                    {
                        var layArray = string.Join(",", layArrivalSeconds);
                        var backArray = string.Join(",", backArrivalSeconds);
                        this.debugLogger.Error($"Failed to get intensity ratio {ex}");
                    }
                }

                var filledOrders = orders.Where(o => o.Status == OrderStatus.Filled).ToArray();
                var inventory = 0M;
                inventory -= filledOrders.Where(o => o.Side == OrderSide.Back).Sum(o => o.SizeFilled * o.Price);
                inventory += filledOrders.Where(o => o.Side == OrderSide.Lay).Sum(o => o.SizeFilled * o.Price);
                inventory = inventory / (inventory >= 0 ? bestToBack.Market.Price : bestToLay.Market.Price);

                inventory = Math.Round(inventory, 2);
                this.Inventory = inventory;
                this.OnPropertyChagned("Inventory");

                var pnl = this.outcome.CalculatePnl(bestToBack.Market.Price, bestToLay.Market.Price);
                pnl = Math.Round(pnl, 4);
                this.PnL = pnl;
                this.OnPropertyChagned("PnL");
                var totalTraded = 0M;
                if(book.Traded.Count > 0)
                {
                    totalTraded = book.Traded.Values.Sum();
                }

                if(this.TotalTraded!=0 && totalTraded != this.TotalTraded)
                {
                    var totalTradedVolChange = totalTraded - this.TotalTraded;
                    var tradedVolChange = tradeChanges.Sum(x => x.NewSize - x.OldSize);
                    if(tradedVolChange!= totalTradedVolChange)
                    {
                        debugLogger.Warn($"{this.market.Id}-{this.outcome.Id} totalTradedVolChange {totalTradedVolChange} is different from tradedVolChange {tradedVolChange}");
                    }
                }

                this.TotalTraded = totalTraded;

                this.context.Log(string.Join(",", new[]
                {
                        "OnOutcomeChange",
                        change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        bestToBack.Market.Size.ToString(),
                        bestToBack.Market.Price.ToString(),
                        bestToLay.Market.Price.ToString(),
                        bestToLay.Market.Size.ToString(),
                        totalTraded.ToString(),
                        this.DepthImbalance.ToString(),
                        inventory.ToString(),
                        filledOrders.Length.ToString(),
                        this.LayOrderVolume.ToString(),
                        this.BackOrderVolume.ToString(),
                        pnl.ToString(),
                        this.State.ToString()
                    }));

                if (inventory == 0 && this.DepthImbalance != 0)
                {
                    if(this.DepthImbalance < 0.2)
                    {
                        newOrders.Add(new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Lay, bestToLay.Market.Price, this.minOrderSize));
                    }
                    else if(this.DepthImbalance > 5)
                    {
                        newOrders.Add(new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Back, bestToBack.Market.Price, this.minOrderSize));
                    }
                }
                else if (inventory != 0 && change.Timestamp - this.lastOrderTimestamp > TimeSpan.FromSeconds(10))
                {
                    // Close after 10 seconds
                    if(inventory > 0)
                    {
                        newOrders.Add(new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Back, bestToBack.Market.Price, inventory));
                    }
                    else if(inventory < 0)
                    {
                        newOrders.Add(new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Lay, bestToLay.Market.Price, -inventory));
                    }
                }
            }


            if (this.State == AgentState.Running)
            {
                foreach (var order in clearOrders)
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
                        foreach (var order in newOrders)
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
