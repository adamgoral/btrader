using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BTrader.UI.ViewModels
{
    public class OutcomeSummaryGroupViewModel : BaseViewModel
    {

        public ObservableCollection<OutcomeSummaryViewModel> Outcomes { get; } = new ObservableCollection<OutcomeSummaryViewModel>();
        private readonly Dictionary<string, OutcomeSummaryViewModel> outcomeLookup = new Dictionary<string, OutcomeSummaryViewModel>();
        private readonly Dictionary<string, IDisposable> subscriptions = new Dictionary<string, IDisposable>();

        public OutcomeSummaryViewModel Add(Market market, Outcome outcome, string exchange, Dispatcher dispatcher, IObserver<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection)
        {
            OutcomeSummaryViewModel result;
            if (!this.outcomeLookup.TryGetValue(exchange, out result))
            {
                this.subscriptions[exchange] = outcome.Changes.Subscribe(_ => this.OnChange());
                result = new OutcomeSummaryViewModel(market, outcome, exchange, dispatcher, marketAndOutcomeSelection);
                this.Outcomes.Add(result);
                this.OnChange();
            }

            return result;
        }

        private void OnChange()
        {
            var toLayPrices = this.Outcomes.SelectMany(o => o.Outcome.OrderBook.Clone().ToLay.Keys).OrderBy(p => p).ToArray();
            var toBackPrices = this.Outcomes.SelectMany(o => o.Outcome.OrderBook.Clone().ToBack.Keys).OrderByDescending(p => p).ToArray();
        }

        public void Remove(string exchange)
        {
            IDisposable subscription;
            if(this.subscriptions.TryGetValue(exchange, out subscription))
            {
                subscription.Dispose();
                this.subscriptions.Remove(exchange);
            }
            OutcomeSummaryViewModel result;
            if(this.outcomeLookup.TryGetValue(exchange, out result))
            {
                this.Outcomes.Remove(result);
                this.outcomeLookup.Remove(exchange);
            }
        }
    }
}
