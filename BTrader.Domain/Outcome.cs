using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class Outcome
    {

        public static Outcome FromObservation(OutcomeObservation observation, IObservable<OutcomeChange> changes)
        {
            return new Outcome(observation.Timestamp, observation.Id, observation.Name, observation.Status.Value, OrderBook.FromObservation(observation.OrderBook), observation.Orders, changes);
        }

        private readonly Dictionary<string, Order> orders = new Dictionary<string, Order>();

        public Outcome(DateTime timestamp, string id, string name, OutcomeStatus status, OrderBook orderBook, IEnumerable<Order> orders, IObservable<OutcomeChange> changes)
        {
            this.Id = id;
            this.Name = name;
            this.Status = status;
            this.OrderBook = orderBook;
            this.LastUpdate = timestamp;
            this.Changes = changes;
            foreach (var order in orders)
            {
                this.orders.Add(order.Id, order);
            }
        }
        public string Id { get; }
        public string Name { get; }

        public OutcomeStatus Status { get; private set; }

        public OrderBook OrderBook { get; }
        public DateTime LastUpdate { get; private set; }

        public IReadOnlyDictionary<string, Order> Orders
        {
            get
            {
                lock (this.orders)
                {
                    return this.orders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
            }
        }

        public decimal CalculatePnl(decimal bestToBack, decimal bestToLay)
        {
            Order[] orders = null;
            lock (this.orders)
            {
                orders = this.orders.Values.ToArray();
            }

            var filledOrders = orders.Where(o => o.Status == OrderStatus.Filled).ToArray();
            var backOrders = filledOrders.Where(o => o.Side == OrderSide.Back).ToArray();
            var layOrders = filledOrders.Where(o => o.Side == OrderSide.Lay).ToArray();
            var pnlAtBestBackLevel =
                + backOrders.Sum(o => o.SizeFilled * (o.Price / bestToBack - 1))
                + layOrders.Sum(o => o.SizeFilled * (1 - o.Price / bestToBack));
            var pnlAtBestLayLevel =
                + backOrders.Sum(o => o.SizeFilled * (o.Price / bestToLay - 1))
                + layOrders.Sum(o => o.SizeFilled * (1 - o.Price / bestToLay));
            var pnl = Math.Min(pnlAtBestBackLevel, pnlAtBestLayLevel);

            //if(Math.Abs(pnl - previousPnl) > 1)
            //{
            //    DumpOrders(previousOrders, orders);
            //}

            //previousPnl = pnl;
            //previousOrders = orders;

            return pnl;
        }

        //private Order[] previousOrders;
        //private decimal previousPnl = 0M;

        //public void DumpOrders(Order[] before, Order[] after)
        //{
        //    DumpOrders(before, @"D:\Data\btrader\reports\hfttests\orderdumpbefore.csv");
        //    DumpOrders(after, @"D:\Data\btrader\reports\hfttests\orderdumpafter.csv");
        //}

        //public void DumpOrders(Order[] orders, string fileName)
        //{
        //    using(var writer = new StreamWriter(fileName, false))
        //    {
        //        foreach(var order in orders)
        //        {
        //            writer.WriteLine(string.Join(",", new []
        //            {
        //                order.CreatedOn.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        //                order.Id,
        //                order.MarketId,
        //                order.Side.ToString(),
        //                order.Price.ToString(),
        //                order.Size.ToString(),
        //                order.SizeFilled.ToString(),
        //                order.Status.ToString()
        //            }));
        //        }

        //        writer.Flush();
        //    }
        //}

        public IObservable<OutcomeChange> Changes { get; }

        internal OutcomeChange OnChange(OutcomeObservation change)
        {
            this.LastUpdate = change.Timestamp;
            lock (this.orders)
            {
                if (change.FullSnap)
                {
                    this.orders.Clear();
                }

                foreach (var order in change.Orders)
                {
                    Domain.Order existing;
                    if(!this.orders.TryGetValue(order.Id, out existing))
                    {
                        this.orders[order.Id] = order;
                    }
                    else if(existing.Status == OrderStatus.Open)
                    {
                        // only update open orders in case of updates arrving out of order
                        this.orders[order.Id] = order;
                    }
                }
            }

            if (change.Status != null)
            {
                this.Status = change.Status.Value;
            }
            OrderBookChange orderBookChange = null;
            if (change.OrderBook != null)
            {
                orderBookChange = this.OrderBook.Update(change.OrderBook);
            }
            
            return new OutcomeChange(change.Id, change.Timestamp, this.Status, orderBookChange, change.Orders);
        }
    }
}