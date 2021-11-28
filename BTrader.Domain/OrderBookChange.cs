using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BTrader.Domain
{
    public class OrderBookChange
    {
        public OrderBookChange(IEnumerable<PriceSizeChange> toLayChanges, IEnumerable<PriceSizeChange> toBackChanges, IEnumerable<PriceSizeChange> tradedChanges)
        {
            this.ToLay = new ReadOnlyCollection<PriceSizeChange>(toLayChanges.ToList());
            this.ToBack = new ReadOnlyCollection<PriceSizeChange>(toBackChanges.ToList());
            this.Traded = new ReadOnlyCollection<PriceSizeChange>(tradedChanges.ToList());
        }

        public IReadOnlyCollection<PriceSizeChange> ToLay { get; }
        public IReadOnlyCollection<PriceSizeChange> ToBack { get; }
        public IReadOnlyCollection<PriceSizeChange> Traded { get; }
    }
}