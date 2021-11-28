using System;
using System.Text;

namespace BTrader.Domain
{
    public class Order
    {
        public Order(string id, string marketId, string outcomeId, DateTime createdOn, OrderSide side, OrderStatus status, decimal price, decimal size, decimal sizeFilled)
        {
            Id = id;
            MarketId = marketId;
            OutcomeId = outcomeId;
            CreatedOn = createdOn;
            Side = side;
            Status = status;
            Price = price;
            Size = size;
            SizeFilled = sizeFilled;
        }

        public string Id { get; }
        public OrderSide Side { get; }
        public OrderStatus Status { get; }
        public DateTime CreatedOn { get; }
        public decimal Price { get; }
        public decimal Size { get; }
        public decimal SizeFilled { get; }
        public string MarketId { get; }
        public string OutcomeId { get; }
    }
}