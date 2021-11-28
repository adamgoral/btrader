using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class PriceSizeChange
    {
        public PriceSizeChange(decimal price, decimal oldSize, decimal newSize)
        {
            Price = price;
            OldSize = oldSize;
            NewSize = newSize;
        }

        public decimal Price { get; }
        public decimal OldSize { get; }
        public decimal NewSize { get; }

        public override string ToString()
        {
            return $"{this.OldSize}->{this.NewSize}@{this.Price}";
        }
    }
}