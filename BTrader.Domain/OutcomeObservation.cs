using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BTrader.Domain
{
    public class OutcomeObservation
    {

        public OutcomeObservation(string id, DateTime timestamp, string name, OutcomeStatus? status, OrderBookObservation orderBook, IEnumerable<Order> orders, decimal? handicap, bool fullSnap)
        {
            this.Id = id;
            Timestamp = timestamp;
            this.Name = name;
            this.Status = status;
            this.OrderBook = orderBook;
            FullSnap = fullSnap;
            this.Orders = new ReadOnlyCollection<Order>(orders.ToList());
            this.Handicap = handicap;
        }

        public string Id { get; }
        public string Name { get; }
        public DateTime Timestamp { get; }
        public OutcomeStatus? Status { get; }
        public IReadOnlyCollection<Order> Orders { get; }
        public OrderBookObservation OrderBook { get; }
        public bool FullSnap { get; }
        public decimal? Handicap { get; }
    }
}