using System;
using System.Threading;

namespace BTrader.Domain
{
    public class QueuedOrder
    {
        public static int id = 0;

        public static QueuedOrder FromOrderRequest(DateTime timestamp, decimal queuePosition, OrderRequest request)
        {
            var result = new QueuedOrder(timestamp, request.MarketId, request.OutcomeId, queuePosition, request.Side, request.Price, request.Size);
            return result;
        }

        public Order ToOrder()
        {
            decimal sizeFilled = 0;
            if (this.Status == OrderStatus.Filled)
            {
                sizeFilled = this.Size;
            }
            var result = new Order(this.Id.ToString(), this.MarketId, this.OutcomeId, this.Timestamp, this.Side, this.Status, this.Price, this.Size, sizeFilled);
            return result;
        }

        public QueuedOrder(DateTime timestamp, string marketId, string outcomeId, decimal queuePosition, OrderSide side, decimal price, decimal size)
        {
            this.Timestamp = timestamp;
            MarketId = marketId;
            OutcomeId = outcomeId;
            this.QueuePosition = queuePosition;
            Side = side;
            this.Id = Interlocked.Increment(ref id);
            this.Price = price;
            this.Size = size;
            this.Status = OrderStatus.Open;
        }

        public int Id { get; }

        public decimal QueuePosition { get; set; }
        public OrderSide Side { get; }
        public DateTime Timestamp { get; }
        public string MarketId { get; }
        public string OutcomeId { get; }
        public OrderStatus Status { get; set; }

        public decimal Price { get; }
        public decimal Size { get; }
    }

}
