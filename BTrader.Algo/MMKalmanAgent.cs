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
    public class MMKalmanAgent : IDisposable, INotifyPropertyChanged
    {
        private readonly AgentContext context;
        private readonly ISession session;
        private readonly Market market;
        private readonly Outcome outcome;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly ILog log = new DebugLogger();
        private readonly int depth = 10;
        private readonly decimal tradeSize = 2;
        private readonly decimal minOrderSize = 2;
        private DateTime lastOrderTimestamp = DateTime.MinValue;
        private TimeSpan delayBeforeNextOrder = TimeSpan.FromMilliseconds(500);
        private TimeSpan exitTimeBeforeEventStart = TimeSpan.FromMinutes(2);
        private AgentState _state;
        private readonly object updateLock = new object();
        private readonly List<int> priceIndex;
        private readonly int orderSpacing;
        private readonly int orderDepth;

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

        public MMKalmanAgent(AgentContext context, int orderSpacing, int orderDepth, Market market, Outcome outcome)
        {
            this.context = context;
            this.session = context.Sessions["Betfair"];
            this.market = market;
            this.outcome = outcome;
            this.Id = $"MMKalman-{market.Id}-{outcome.Id}";
            this.orderSpacing = orderSpacing;
            this.orderDepth = orderDepth;
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
                "LastTradedPrice",
                "LastTradedVoume",
                "EstimatedPrice",
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
        public double EstimatedPrice { get; private set; }
        public double LastTradedPrice { get; private set; }
        public double LastTradedVolume { get; private set; }
        public decimal BackOrderVolume { get; private set; }
        public decimal LayOrderVolume { get; private set; }

        public decimal BackOrderCount { get; private set; }
        public decimal LayOrderCount { get; private set; }

        public HFTRule Rule { get; private set; }

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
            return this.GetBestPrices(orderedMarket, depth, backOrders);
        }

        private AgentPriceSize[] GetBestToBack(OrderBook orderBook, int depth, Order[] orders)
        {
            var layOrders = orders.Where(o => o.Status == OrderStatus.Open && o.Side == OrderSide.Lay);
            var orderedMarket = orderBook.ToBack.OrderByDescending(o => o.Key);
            return this.GetBestPrices(orderedMarket, depth, layOrders);
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

        private OrderBook previous;
        private double xPrevious = 0.0;
        private double pPrevious = 1.0;
        private double k = 0.0;

        private double GetMeanEstimate(double observedPrice, double volume, double volumeMax)
        {
            var q = 0.00001;
            var p = pPrevious + q;
            var r = p * Math.Max(0, (1 - volume / volumeMax));
            var k = p / (p + r);
            var xhat = xPrevious + k * (observedPrice - xPrevious);
            this.pPrevious = (1 - k) * p;
            this.xPrevious = xhat;
            return xhat;
        }

        private PriceSize? GetTraded(OrderBook current)
        {
            PriceSize? result = null;
            if (previous != null)
            {
                var sum = 0M;
                var weight = 0M;
                foreach(var kvp in current.Traded)
                {
                    decimal existing;
                    if(previous.Traded.TryGetValue(kvp.Key, out existing))
                    {
                        var diff = kvp.Value - existing;
                        sum += kvp.Key * diff;
                        weight += diff;
                    }
                    else
                    {
                        sum += kvp.Key * existing;
                        weight += existing;
                    }
                }

                if(weight != 0)
                {
                    result = new PriceSize(sum / weight, weight);
                }
            }

            this.previous = current;
            return result;
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

            var orderBook = this.outcome.OrderBook.Clone();
            var bestToLayArray = this.GetBestToLay(orderBook, this.depth, openOrders);
            var bestToBackArray = this.GetBestToBack(orderBook, this.depth, openOrders);


            var traded = this.GetTraded(orderBook);
            if (bestToBackArray.Any() && bestToLayArray.Any())
            {
                var bestToBack = bestToBackArray.First();
                var bestToLay = bestToLayArray.First();
                if (this.xPrevious == 0)
                {
                    this.xPrevious = (double)(bestToBack.Market.Price * bestToBack.Market.Size + bestToLay.Market.Price * bestToLay.Market.Size) / (double)(bestToBack.Market.Size + bestToLay.Market.Size);
                }

                if (traded != null)
                {
                    this.LastTradedPrice = (double)traded.Value.Price;
                    this.LastTradedVolume = (double)traded.Value.Size;
                    this.OnPropertyChagned("LastTradedPrice");
                    this.OnPropertyChagned("LastTradedVolume");
                    this.EstimatedPrice = this.GetMeanEstimate(this.LastTradedPrice, this.LastTradedVolume, (double)orderBook.Traded.Values.Sum());
                    this.OnPropertyChagned("EstimatedPrice");
                }

                this.context.Log(string.Join(",", new[]
{
                    "OnOutcomeChange",
                    change.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    bestToBack.Market.Size.ToString(),
                    bestToBack.Market.Price.ToString(),
                    bestToLay.Market.Price.ToString(),
                    bestToLay.Market.Size.ToString(),
                    this.LastTradedPrice.ToString(),
                    this.LastTradedVolume.ToString(),
                    this.EstimatedPrice.ToString(),
                    this.State.ToString()
                }));
            }
            
            System.Diagnostics.Debug.WriteLine($"traded {traded}");

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
