using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class OutcomeChange
    {
        public OutcomeChange(string id, DateTime timestamp, OutcomeStatus status, OrderBookChange orderBookChange, IEnumerable<Order> orders)
        {
            this.Id = id;
            this.Timestamp = timestamp;
            this.Status = status;
            this.OrderBook = orderBookChange;
            this.Orders = new ReadOnlyCollection<Order>(orders.ToList());
        }

        public string Id { get; }
        public DateTime Timestamp { get; }
        public OutcomeStatus Status { get; }
        public OrderBookChange OrderBook { get; }
        public ReadOnlyCollection<Order> Orders { get; }
    }
}