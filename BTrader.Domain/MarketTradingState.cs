using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Domain
{

    public class MarketTradingState : IDisposable
    {
        private readonly Func<DateTime> timestampProvider;
        private readonly IObserver<MarketObservation> orderObserver;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly Queue<QueuedOrder> cancellationQueue = new Queue<QueuedOrder>();
        private OrderBook previousState;
        private StreamWriter writer;

        public MarketTradingState(string baseDir, string id, Func<DateTime> timestampProvider, Outcome outcome, IObserver<MarketObservation> orderObserver)
        {
            this.writer = new StreamWriter(baseDir + $@"\{id}.trades.csv", false);
            this.disposables.Add(this.writer);
            this.writer.WriteLine(string.Join(",", new[]
            {
                    "timestamp", "id", "side", "price", "size", "action", "estQueuePos"
            }));
            this.writer.Flush();

            this.timestampProvider = timestampProvider;
            this.orderObserver = orderObserver;
            // TODO come up with better approach of avoiding stack overflow (order update triggering update in this class)
            this.disposables.Add(outcome.Changes
                .Where(o => o.Orders.Count == 0)
                .Subscribe(change => this.Update(change.Timestamp, outcome.OrderBook.Clone())));
        }

        public decimal Position
        {
            get
            {
                var layPositions = this.LayOrders.Where(p => p.Status == OrderStatus.Filled).Sum(p => p.Size);
                var backPositions = -this.BackOrders.Where(p => p.Status == OrderStatus.Filled).Sum(p => p.Size);
                return layPositions + backPositions;
            }
        }
        public List<QueuedOrder> LayOrders { get; } = new List<QueuedOrder>();

        internal void CancelOrder(Order order)
        {
            var existing = this.LayOrders.FirstOrDefault(o => o.Id.ToString() == order.Id && o.Status == OrderStatus.Open);
            if(existing != null)
            {
                this.cancellationQueue.Enqueue(existing);
                return;
            }

            existing = this.BackOrders.FirstOrDefault(o => o.Id.ToString() == order.Id && o.Status == OrderStatus.Open);
            if (existing != null)
            {
                this.cancellationQueue.Enqueue(existing);
                return;
            }
        }

        public List<QueuedOrder> BackOrders { get; } = new List<QueuedOrder>();

        private void AddLayOrder(QueuedOrder order)
        {
            this.writer.WriteLine($"{order.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{order.Id},lay,{order.Price},{order.Size},placed,{order.QueuePosition}");
            this.writer.Flush();

            this.LayOrders.Add(order);
        }

        internal IEnumerable<Order> GetOrders()
        {
            throw new NotImplementedException();
        }

        private void AddBackOrder(QueuedOrder order)
        {
            this.writer.WriteLine($"{order.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{order.Id},back,{order.Price},{order.Size},placed,{order.QueuePosition}");
            this.writer.Flush();

            this.BackOrders.Add(order);
        }

        public void Update(DateTime timestamp, OrderBook state)
        {
            var updatedOrders = new List<Order>();

            while (this.cancellationQueue.Any())
            {
                var toCancel = this.cancellationQueue.Dequeue();
                if(toCancel.Status == OrderStatus.Open)
                {
                    toCancel.Status = OrderStatus.Cancelled;
                    this.writer.WriteLine($"{toCancel.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{toCancel.Id},{toCancel.Side},{toCancel.Price},{toCancel.Size},cancelled,{toCancel.QueuePosition}");

                    updatedOrders.Add(toCancel.ToOrder());
                }
            }

            if (previousState != null)
            {
                var tradedDiff = new Dictionary<decimal, decimal>();
                foreach (var kvp in state.Traded)
                {
                    decimal previousVolume;
                    if (previousState.Traded.TryGetValue(kvp.Key, out previousVolume))
                    {
                        tradedDiff[kvp.Key] = kvp.Value - previousVolume;
                    }
                }

                foreach (var layOrder in LayOrders.Where(o => o.Status == OrderStatus.Open))
                {
                    if(state.ToLay.ContainsKey(layOrder.Price))
                    {
                        layOrder.QueuePosition = 0;
                        layOrder.Status = OrderStatus.Filled;
                        this.writer.WriteLine($"{timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{layOrder.Id},lay,{layOrder.Price},{layOrder.Size},filled");
                        this.writer.Flush();

                        updatedOrders.Add(layOrder.ToOrder());
                    }
                    else if(state.ToBack.ContainsKey(layOrder.Price))
                    {
                        decimal tradedVolume = 0;
                        if (tradedDiff.TryGetValue(layOrder.Price, out tradedVolume))
                        {
                            layOrder.QueuePosition -= tradedVolume;
                            if (layOrder.QueuePosition < 0)
                            {
                                layOrder.QueuePosition = 0;
                                layOrder.Status = OrderStatus.Filled;
                                this.writer.WriteLine($"{timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{layOrder.Id},lay,{layOrder.Price},{layOrder.Size},filled");
                                this.writer.Flush();

                                updatedOrders.Add(layOrder.ToOrder());
                            }
                        }
                    }
                }

                foreach (var backOrder in BackOrders.Where(o => o.Status == OrderStatus.Open))
                {
                    if (state.ToBack.ContainsKey(backOrder.Price))
                    {
                        backOrder.QueuePosition = 0;
                        backOrder.Status = OrderStatus.Filled;
                        this.writer.WriteLine($"{timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{backOrder.Id},back,{backOrder.Price},{backOrder.Size},filled");
                        this.writer.Flush();

                        updatedOrders.Add(backOrder.ToOrder());
                    }
                    else if(state.ToLay.ContainsKey(backOrder.Price))
                    {
                        decimal tradedVolume = 0;
                        if (tradedDiff.TryGetValue(backOrder.Price, out tradedVolume))
                        {
                            backOrder.QueuePosition -= tradedVolume;
                            if (backOrder.QueuePosition < 0)
                            {
                                backOrder.QueuePosition = 0;
                                backOrder.Status = OrderStatus.Filled;
                                this.writer.WriteLine($"{timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")},{backOrder.Id},back,{backOrder.Price},{backOrder.Size},filled");
                                this.writer.Flush();

                                updatedOrders.Add(backOrder.ToOrder());
                            }
                        }
                    }
                }

                if (updatedOrders.Any())
                {
                    var marketId = updatedOrders.First().MarketId;
                    this.orderObserver.OnNext(updatedOrders.ToMarketObservation(marketId, timestamp, false));
                }
            }

            previousState = state;
        }

        internal void PlaceOrders(IEnumerable<OrderRequest> orderRequests)
        {
            var ordersToAck = new List<Order>();
            foreach (var orderRequest in orderRequests)
            {
                decimal queuePosition = 0;
                if (orderRequest.Side == OrderSide.Back)
                {
                    if (this.previousState != null)
                    {
                        this.previousState.ToLay.TryGetValue(orderRequest.Price, out queuePosition);
                    }
                    var queuedOrder = QueuedOrder.FromOrderRequest(this.timestampProvider(), queuePosition, orderRequest);
                    this.AddBackOrder(queuedOrder);
                    ordersToAck.Add(queuedOrder.ToOrder());
                }
                else
                {
                    if (this.previousState != null)
                    {
                        this.previousState.ToBack.TryGetValue(orderRequest.Price, out queuePosition);
                    }
                    var queuedOrder = QueuedOrder.FromOrderRequest(this.timestampProvider(), queuePosition, orderRequest);
                    this.AddLayOrder(queuedOrder);
                    ordersToAck.Add(queuedOrder.ToOrder());
                }
            }

            var marketId = ordersToAck.First().MarketId;
            this.orderObserver.OnNext(ordersToAck.ToMarketObservation(marketId, this.timestampProvider(), false));
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }

        internal decimal CalculatePnl(decimal bestToBack, decimal bestToLay)
        {
            var layPnl = this.LayOrders.Where(o => o.Status == OrderStatus.Filled).Sum(o => (1 - bestToBack / o.Price) * o.Size);
            var backPnl = this.BackOrders.Where(o => o.Status == OrderStatus.Filled).Sum(o => (bestToLay / o.Price - 1) * o.Size);
            return layPnl + backPnl;
        }
    }

}
