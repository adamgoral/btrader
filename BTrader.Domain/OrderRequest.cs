using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class OrderRequest
    {
        public OrderRequest(string marketId, string outcomeId, OrderSide side, decimal price, decimal size)
        {
            MarketId = marketId;
            OutcomeId = outcomeId;
            Side = side;
            Price = price;
            Size = Math.Round(size, 2);
        }

        public string MarketId { get; set; }
        public string OutcomeId { get; set; }
        public OrderSide Side { get; set; }
        public decimal Price { get; set; }
        public decimal Size { get; set; }
    }
}