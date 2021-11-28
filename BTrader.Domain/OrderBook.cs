using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class OrderBook
    {
        public static OrderBook FromObservation(OrderBookObservation observation)
        {
            return new OrderBook(observation.ToLay, observation.ToBack, observation.Traded);
        }

        private readonly SortedDictionary<decimal, decimal> toLay = new SortedDictionary<decimal, decimal>();
        private readonly SortedDictionary<decimal, decimal> toBack = new SortedDictionary<decimal, decimal>();
        private readonly SortedDictionary<decimal, decimal> traded = new SortedDictionary<decimal, decimal>();
        public OrderBook(IEnumerable<PriceSize> toLay, IEnumerable<PriceSize> toBack, IEnumerable<PriceSize> traded)
        {
            this.Update(this.toLay, toLay, false).ToArray();
            this.Update(this.toBack, toBack, false).ToArray();
            this.Update(this.traded, traded, false).ToArray();
        }

        public OrderBook Clone()
        {
            lock (this)
            {
                return new OrderBook(
                    this.toLay.Select(p => new PriceSize(p.Key, p.Value)),
                    this.toBack.Select(p => new PriceSize(p.Key, p.Value)),
                    this.traded.Select(p => new PriceSize(p.Key, p.Value)));
            }
        }

        private IEnumerable<PriceSizeChange> Update(IDictionary<decimal, decimal> dictionary, IEnumerable<PriceSize> changes, bool fullSnap)
        {
            var cached = changes.ToArray();
            foreach(var change in cached)
            {
                decimal existing;
                dictionary.TryGetValue(change.Price, out existing);
                if (existing != change.Size)
                {
                    if (change.Size == 0)
                    {
                        dictionary.Remove(change.Price);
                    }
                    else
                    {
                        dictionary[change.Price] = change.Size;
                    }

                    yield return new PriceSizeChange(change.Price, existing, change.Size);
                }
            }

            if (fullSnap)
            {
                var lookup = new HashSet<decimal>(cached.Select(p => p.Price));
                foreach(var kvp in dictionary.ToArray())
                {
                    if (!lookup.Contains(kvp.Key))
                    {
                        dictionary.Remove(kvp.Key);
                        yield return new PriceSizeChange(kvp.Key, kvp.Value, 0);
                    }
                }
            }
        }

        public IReadOnlyDictionary<decimal, decimal> ToLay => this.toLay;
        public IReadOnlyDictionary<decimal, decimal> ToBack => this.toBack;

        public OrderBookChange Update(OrderBookObservation change)
        {
            lock (this)
            {
                var toLayChanges = this.Update(this.toLay, change.ToLay, change.FullSnap).ToArray();
                var toBackChanges = this.Update(this.toBack, change.ToBack, change.FullSnap).ToArray();
                var tradedChanges = this.Update(this.traded, change.Traded, change.FullSnap).ToArray();
                return new OrderBookChange(toLayChanges, toBackChanges, tradedChanges);
            }
        }

        public IReadOnlyDictionary<decimal, decimal> Traded => this.traded;
    }
}