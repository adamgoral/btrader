using BTrader.Domain;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Threading;

namespace BTrader.UI.ViewModels
{
    public class PriceSizeOrder
    {
        public PriceSizeOrder(decimal price, decimal size, decimal order)
        {
            Price = price;
            Size = size;
            Order = order;
        }

        public decimal Price { get; }
        public decimal Size { get; }
        public decimal Order { get; }
    }

    public class OutcomeSummaryViewModel : BaseViewModel, IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly Market market;
        private readonly IObserver<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection;

        public OutcomeSummaryViewModel(Market market, Outcome outcome, string exchange, Dispatcher dispatcher, IObserver<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection)
        {
            this.Name = outcome.Name;
            this.market = market;
            Outcome = outcome;
            this.Exchange = exchange;
            this.marketAndOutcomeSelection = marketAndOutcomeSelection;
            this.OnChange(outcome);
            this.disposables.Add(outcome.Changes.ObserveOn(dispatcher).Subscribe(oc => this.OnChange(outcome)));
            this.AssignAgentCommand = new DelegateCommand(o => exchange == "Betfair", this.OnAssignAgent);
        }

        private void OnAssignAgent(object value)
        {
            this.marketAndOutcomeSelection.OnNext(new MarketAndOutcomeSelectionArgs(market, this.Outcome));
        }

        private void OnChange(Outcome outcome)
        {
            this.Status = outcome.Status;
            this.LastUpdate = outcome.LastUpdate;
            var orderBook = outcome.OrderBook.Clone();
            this.Volume = orderBook.Traded.Sum(p => p.Value);
            var bestToBacks = orderBook.ToBack.OrderByDescending(i => i.Key).ToArray();
            var openOrders = outcome.Orders.Values.Where(o => o.Status == OrderStatus.Open).ToArray();
            if (bestToBacks.Length > 0)
            {
                var price = bestToBacks[0];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToBack1 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToBack1 = null;
            }
            if (bestToBacks.Length > 1)
            {
                var price = bestToBacks[1];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToBack2 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToBack2 = null;
            }
            if (bestToBacks.Length > 2)
            {
                var price = bestToBacks[2];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToBack3 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToBack3 = null;
            }

            var bestToLays = orderBook.ToLay.OrderBy(i => i.Key).ToArray();
            if (bestToLays.Length > 0)
            {
                var price = bestToLays[0];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToLay1 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToLay1 = null;
            }
            if (bestToLays.Length > 1)
            {
                var price = bestToLays[1];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToLay2 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToLay2 = null;
            }
            if (bestToLays.Length > 2)
            {
                var price = bestToLays[2];
                var orderSize = openOrders.Where(o => o.Price == price.Key).Sum(o => o.Size - o.SizeFilled);
                this.BestToLay3 = new PriceSizeOrder(price.Key, price.Value, orderSize);
            }
            else
            {
                this.BestToLay3 = null;
            }

            this.OnPropertyChanged("Status");
            this.OnPropertyChanged("Volume");
            this.OnPropertyChanged("LastUpdate");
            this.OnPropertyChanged("BestToBack1");
            this.OnPropertyChanged("BestToBack2");
            this.OnPropertyChanged("BestToBack3");
            this.OnPropertyChanged("BestToLay1");
            this.OnPropertyChanged("BestToLay2");
            this.OnPropertyChanged("BestToLay3");
        }

        public string Name { get; }
        public Outcome Outcome { get; }
        public string Exchange { get; }
        public OutcomeStatus Status { get; private set; }
        public DateTime LastUpdate { get; private set; }
        public decimal Volume { get; private set; }
        public PriceSizeOrder BestToBack1 { get; private set; }
        public PriceSizeOrder BestToBack2 { get; private set; }
        public PriceSizeOrder BestToBack3 { get; private set; }
        public PriceSizeOrder BestToLay1 { get; private set; }
        public PriceSizeOrder BestToLay2 { get; private set; }
        public PriceSizeOrder BestToLay3 { get; private set; }
        public DelegateCommand AssignAgentCommand { get; }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}
