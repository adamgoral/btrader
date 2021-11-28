using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BTrader.Domain
{
    public class OrderBookObservation
    {
        public OrderBookObservation(IEnumerable<PriceSize> toLay, IEnumerable<PriceSize> toBack, IEnumerable<PriceSize> traded, decimal? volume, bool fullSnap)
        {
            this.ToLay = new ReadOnlyCollection<PriceSize>(toLay == null ? new List<PriceSize>() : toLay.ToList());
            this.ToBack = new ReadOnlyCollection<PriceSize>(toBack == null ? new List<PriceSize>() : toBack.ToList());
            this.Traded = new ReadOnlyCollection<PriceSize>(traded == null ? new List<PriceSize>() : traded.ToList());
            this.Volume = volume;
            FullSnap = fullSnap;
        }

        public IReadOnlyCollection<PriceSize> ToLay { get; }
        public IReadOnlyCollection<PriceSize> ToBack { get; }
        public IReadOnlyCollection<PriceSize> Traded { get; }
        public decimal? Volume { get; }
        public bool FullSnap { get; }
    }
}