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

    public class MMAgent : IDisposable, INotifyPropertyChanged
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
        private readonly List<decimal> ladder;
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

        public MMAgent(AgentContext context, int orderSpacing, int orderDepth, Market market, Outcome outcome)
        {
            this.ladder = GetLadderLevels().ToList();
            this.priceIndex = Enumerable.Range(0, this.ladder.Count).Select(v => v * orderSpacing).Intersect(Enumerable.Range(0, this.ladder.Count)).ToList();
            this.context = context;
            this.session = context.Sessions["Betfair"];
            this.market = market;
            this.outcome = outcome;
            this.Id = $"MM-{market.Id}-{outcome.Id}";
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

            var filledOrders = orders.Where(o => o.Status == OrderStatus.Filled).ToArray();


            var book = this.outcome.OrderBook.Clone();
            var bestToLayArray = this.GetBestToLay(book, this.depth, openOrders);
            var bestToBackArray = this.GetBestToBack(book, this.depth, openOrders);

            if (bestToBackArray.Any() && bestToLayArray.Any())
            {
                var bestToBack = bestToBackArray.First();
                var bestToLay = bestToLayArray.First();

                var inventory = 0M;
                inventory -= filledOrders.Where(o => o.Side == OrderSide.Back).Sum(o => o.SizeFilled * o.Price);
                inventory += filledOrders.Where(o => o.Side == OrderSide.Lay).Sum(o => o.SizeFilled * o.Price);
                inventory = inventory / (inventory >= 0 ? bestToBack.Market.Price : bestToLay.Market.Price);
                this.Inventory = inventory;
                this.OnPropertyChagned("Inventory");

                var bestBackIndexPos = this.ladder.IndexOf(bestToLay.Market.Price);
                var backIndex = this.priceIndex.Where(v => v >= bestBackIndexPos).Take(this.orderDepth).ToList();
                var bestLayIndexPos = this.ladder.IndexOf(bestToBack.Market.Price);
                var layIndex = this.priceIndex.Where(v => v <= bestLayIndexPos).Reverse().Take(this.orderDepth).ToList();
                var lastFilled = filledOrders.OrderByDescending(o => o.Id).FirstOrDefault();
                //TODO improve inventory check to deal with small positions
                if (lastFilled != null && lastFilled.SizeFilled == this.tradeSize)
                {
                    if (lastFilled.Price == this.ladder[backIndex[0]])
                    {
                        backIndex.RemoveAt(0);
                    }

                    if ((lastFilled.Price == this.ladder[layIndex[0]]))
                    {
                        layIndex.RemoveAt(0);
                    }
                }


                var backPrices = backIndex.Select(i => this.ladder[i]).ToList();
                var layPrices = layIndex.Select(i => this.ladder[i]).ToList();

                System.Diagnostics.Debug.WriteLine($"last traded @ {lastFilled?.Price}, backs: {string.Join(",", backPrices)}, lays: {string.Join(",", layPrices)}");

                var openBacks = openOrders.Where(o => o.Side == OrderSide.Back).ToArray();
                var openLays = openOrders.Where(o => o.Side == OrderSide.Lay).ToArray();

                clearOrders.AddRange(openBacks.Where(o => !backPrices.Contains(o.Price)));
                clearOrders.AddRange(openLays.Where(o => !layPrices.Contains(o.Price)));

                foreach(var price in backPrices)
                {
                    if(!openBacks.Any(o => o.Price == price))
                    {
                        var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Back, price, this.tradeSize);
                        newOrders.Add(newOrder);
                    }
                }

                foreach(var price in layPrices)
                {
                    if (!openLays.Any(o => o.Price == price))
                    {
                        var newOrder = new OrderRequest(this.market.Id, this.outcome.Id, OrderSide.Lay, price, this.tradeSize);
                        newOrders.Add(newOrder);
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

        public static IEnumerable<decimal> GetLadderLevels()
        {
            var increment = 0.01M;
            var price = 1M;
            while (price < 1000)
            {
                price += increment;
                if (price >= 100M)
                {
                    increment = 10M;
                }
                else if (price >= 20M)
                {
                    increment = 1M;
                }
                else if (price >= 10M)
                {
                    increment = 0.5M;
                }
                else if (price >= 6M)
                {
                    increment = 0.2M;
                }
                else if (price >= 4M)
                {
                    increment = 0.1M;
                }
                else if (price >= 3M)
                {
                    increment = 0.05M;
                }
                else if (price >= 2M)
                {
                    increment = 0.02M;
                }
                yield return price;
            }
        }

    }
}
